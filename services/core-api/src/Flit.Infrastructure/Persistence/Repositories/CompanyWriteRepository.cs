using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Flit.Infrastructure.Persistence.Entities.Identity;
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

        // HU #10505 / ADR-0023: "AdminCompany" es ahora un rol del catálogo GLOBAL
        // (security.roles ya no tiene tenant_id — UNIQUE(code, target_entity_type)). Ya NO se
        // crea una fila de rol por cada compañía nueva (violaría esa unicidad); el catálogo trae
        // una única fila "AdminCompany" (target_entity_type = COMPANY) sembrada por migración/seed,
        // con sus permisos ya asignados. La invitación del primer admin de la compañía (POST
        // /api/v1/security/invitations) resuelve ese rol global por Code, no por tenant.
        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.NewGuid();

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
            // El trigger trg_row_version incrementa row_version en BD; recargar para que la
            // proyección devuelva la versión nueva (concurrencia optimista de la edición).
            await _context.Entry(entity).ReloadAsync(cancellationToken).ConfigureAwait(false);
        }

        return Project(entity);
    }

    public async Task<CompanyListItem?> GetByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Project(entity);
    }

    public async Task<CompanyListItem?> UpdateAsync(
        Guid tenantId,
        string legalName,
        string taxId,
        string tenantType,
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

        // Idempotente: solo persiste si algún campo editable cambia (el code es inmutable).
        var changed =
            entity.LegalName != legalName
            || entity.TaxId != taxId
            || entity.TenantType != tenantType
            || entity.IsActive != isActive;

        if (changed)
        {
            entity.LegalName = legalName;
            entity.TaxId = taxId;
            entity.TenantType = tenantType;
            entity.IsActive = isActive;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedBy = changedBy;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            // El trigger trg_row_version incrementa row_version en BD; recargar para que la
            // proyección devuelva la versión nueva (si no, una edición posterior del mismo
            // cliente daría un 409 falso al reenviar la versión vieja).
            await _context.Entry(entity).ReloadAsync(cancellationToken).ConfigureAwait(false);
        }

        return Project(entity);
    }

    private static CompanyListItem Project(Tenant entity) => new()
    {
        Id = entity.Id,
        Nit = entity.TaxId,
        RazonSocial = entity.LegalName,
        Code = entity.Code,
        TenantType = entity.TenantType,
        EstadoActivo = entity.IsActive,
        FechaCreacion = entity.CreatedAt,
        RowVersion = entity.RowVersion,
    };
}
