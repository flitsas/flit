using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del alta de compañías sobre <c>identity.tenants</c> (#10118).
///
/// La tabla no tiene RLS, así que la inserción es un único <c>SaveChanges</c> (la
/// estrategia de reintentos de Npgsql lo cubre sin transacción manual). Los triggers
/// de BD (<c>tr_tenants_audit</c>, <c>tr_tenants_row_version</c>) registran auditoría
/// y versión automáticamente. La unicidad del <c>code</c> está garantizada por
/// <c>uq_tenants_code</c>; el handler la valida antes para devolver un 422 amigable.
/// </summary>
internal sealed class CompanyWriteRepository : ICompanyWriteRepository
{
    private readonly FlitDbContext _context;

    public CompanyWriteRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        _context.Tenants.AsNoTracking().AnyAsync(t => t.Code == code, cancellationToken);

    public async Task<CompanyListItem> CreateAsync(
        NewCompany company,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        // Load active permissions (excluding rbac.manage which is superadmin-only)
        var permissions = await _context.RbacActions
            .AsNoTracking()
            .Where(a => a.IsActive && a.Slug != "rbac.manage")
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.NewGuid();
        var roleId = Guid.CreateVersion7();

        var entity = new Tenant
        {
            Id = tenantId,
            Code = company.Code,
            LegalName = company.LegalName,
            TaxId = company.TaxId,
            TenantType = company.TenantType,
            IsActive = company.IsActive,
            CreatedAt = now,
            CreatedBy = company.CreatedBy,
        };
        _context.Tenants.Add(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Seed the mandatory "AdminCompany" system role (tenant must exist first for FK)
        _context.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Code = "AdminCompany",
            Name = "Administrador de Compañía",
            IsSystem = true,
            CreatedAt = now,
            CreatedBy = company.CreatedBy,
            RowVersion = 0,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Assign permissions to AdminCompany role (separate save so FK is guaranteed)
        if (permissions.Count > 0)
        {
            _context.RoleGrants.AddRange(permissions.Select(permId => new RoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                RoleId = roleId,
                PermissionId = permId,
                CreatedAt = now,
                CreatedBy = company.CreatedBy,
            }));
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Project(entity);
    }

    public async Task<CompanyListItem?> SetActiveAsync(
        Guid tenantId,
        bool isActive,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        // Idempotente: solo persiste si cambia el estado (evita auditoría/row_version vacíos).
        if (entity.IsActive != isActive)
        {
            entity.IsActive = isActive;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedBy = changedBy;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Project(entity);
    }

    private static CompanyListItem Project(Tenant entity) => new()
    {
        Id = entity.Id,
        Nit = entity.TaxId,
        RazonSocial = entity.LegalName,
        EstadoActivo = entity.IsActive,
        FechaCreacion = entity.CreatedAt,
    };
}
