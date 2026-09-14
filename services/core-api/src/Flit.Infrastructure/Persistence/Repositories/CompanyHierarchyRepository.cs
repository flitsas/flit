using System.Linq.Expressions;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Jerarquía padre-hija sobre <c>identity.tenants</c> (HU #12345, #12355).
/// </summary>
internal sealed class CompanyHierarchyRepository : ICompanyHierarchyRepository
{
    private readonly FlitDbContext _context;

    public CompanyHierarchyRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CompanyHierarchyInfo?> GetHierarchyInfoAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var row = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new CompanyHierarchyInfo(t.Id, t.TenantType, t.IsGroupParent, t.ParentTenantId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row;
    }

    public async Task<IReadOnlyList<CompanyChildListItem>> ListChildrenAsync(
        Guid headTenantId,
        CancellationToken cancellationToken = default)
    {
        return await ChildrenOf(headTenantId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Select(ToChildListItem)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<CompanyChildListItem?> GetChildAsync(
        Guid headTenantId,
        Guid childTenantId,
        CancellationToken cancellationToken = default) =>
        ChildrenOf(headTenantId)
            .Where(t => t.Id == childTenantId)
            .Select(ToChildListItem)
            .FirstOrDefaultAsync(cancellationToken);

    private IQueryable<Tenant> ChildrenOf(Guid headTenantId) =>
        _context.Tenants.AsNoTracking().Where(t => t.ParentTenantId == headTenantId);

    private static Expression<Func<Tenant, CompanyChildListItem>> ToChildListItem =>
        t => new CompanyChildListItem
        {
            Id = t.Id,
            Nit = t.TaxId,
            RazonSocial = t.LegalName,
            Code = t.Code,
            TenantType = t.TenantType,
            EstadoActivo = t.IsActive,
            FechaVinculacion = t.UpdatedAt ?? t.CreatedAt,
            RowVersion = t.RowVersion,
        };

    public async Task<CompanyListItem> CreateChildAsync(
        NewChildCompany company,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.NewGuid();

        var entity = new Tenant
        {
            Id = tenantId,
            Code = company.Code,
            LegalName = company.LegalName,
            TaxId = company.TaxId,
            TenantType = company.TenantType,
            IsGroupParent = false,
            ParentTenantId = company.ParentTenantId,
            IsActive = company.IsActive,
            CreatedAt = now,
            CreatedBy = company.CreatedBy,
        };

        _context.Tenants.Add(entity);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsHierarchyRejection(ex, out var detail))
        {
            _context.ChangeTracker.Clear();
            throw new CompanyLinkRejectedException(tenantId, detail);
        }

        return await ProjectAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompanyListItem?> SetParentAsync(
        Guid childTenantId,
        Guid? parentTenantId,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == childTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        if (entity.ParentTenantId == parentTenantId)
        {
            return await ProjectAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (changedBy is { } actor && actor != Guid.Empty)
        {
            await TenantHierarchySession.SetCurrentUserAsync(_context, actor, cancellationToken).ConfigureAwait(false);
        }

        entity.ParentTenantId = parentTenantId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = changedBy;

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsHierarchyRejection(ex, out var detail))
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _context.ChangeTracker.Clear();
            throw new CompanyLinkRejectedException(childTenantId, detail);
        }

        await _context.Entry(entity).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return await ProjectAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsHierarchyRejection(DbUpdateException ex, out string detail)
    {
        if (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation } pg)
        {
            detail = pg.MessageText;
            return true;
        }

        detail = string.Empty;
        return false;
    }

    private async Task<CompanyListItem> ProjectAsync(Tenant entity, CancellationToken cancellationToken)
    {
        var isOt = await _context.TransitOfficeProfiles
            .AsNoTracking()
            .AnyAsync(p => p.TenantId == entity.Id, cancellationToken)
            .ConfigureAwait(false);

        return new CompanyListItem
        {
            Id = entity.Id,
            Nit = entity.TaxId,
            RazonSocial = entity.LegalName,
            Code = entity.Code,
            TenantType = entity.TenantType,
            IsTransitOffice = isOt,
            EstadoActivo = entity.IsActive,
            FechaCreacion = entity.CreatedAt,
            RowVersion = entity.RowVersion,
        };
    }
}
