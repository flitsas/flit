using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Modules.Platform.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories.Platform;

/// <summary>
/// Lecturas del resolutor de acceso a productos (HU #12965, B-05) sobre <c>identity.tenants</c>,
/// <c>platform.tenant_products</c> y el RBAC. Todo en lectura, sin tracking.
/// </summary>
internal sealed class ProductAccessStore : IProductAccessStore
{
    /// <summary>Tope de niveles al subir por <c>parent_tenant_id</c>: corta un ciclo por datos corruptos.</summary>
    private const int MaxHierarchyDepth = 8;

    private readonly FlitDbContext _context;

    public ProductAccessStore(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<Guid>> GetTenantChainAsync(Guid tenantId, CancellationToken ct)
    {
        var chain = new List<Guid>();
        Guid? current = tenantId;
        while (current is { } id && chain.Count < MaxHierarchyDepth && !chain.Contains(id))
        {
            var row = await _context.Tenants.AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new { t.Id, t.ParentTenantId })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (row is null)
                break;

            chain.Add(row.Id);
            current = row.ParentTenantId;
        }

        // Un ancestro que no se pudo leer, o una cadena cortada por el tope, no puede dar acceso.
        return current is null ? chain : [];
    }

    public async Task<IReadOnlySet<Guid>> GetTenantsWithProductEnabledAsync(IReadOnlyList<Guid> tenantIds, string productCode, CancellationToken ct)
    {
        var ids = await _context.Set<TenantProductEntity>().AsNoTracking()
            .Where(r => tenantIds.Contains(r.TenantId) && r.ProductCode == productCode && r.Enabled)
            .Select(r => r.TenantId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return ids.ToHashSet();
    }

    public async Task<UserProductGrants> GetUserGrantsAsync(Guid userId, Guid tenantId, string productCode, CancellationToken ct)
    {
        var roles = await (
            from a in _context.UserRoleAssignments.AsNoTracking()
            join r in _context.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.UserId == userId && a.TenantId == tenantId && a.DeletedAt == null
                  && r.DeletedAt == null && r.IsActive && r.ProductCode == productCode
            orderby r.Code
            select new { r.Id, r.Code }
        ).ToListAsync(ct).ConfigureAwait(false);

        if (roles.Count == 0)
            return new UserProductGrants([], []);

        var roleIds = roles.Select(r => r.Id).ToList();
        var permissions = await (
            from g in _context.RoleGrants.AsNoTracking()
            join p in _context.RbacActions.AsNoTracking() on g.PermissionId equals p.Id
            join m in _context.SecurityModules.AsNoTracking() on p.ModuleId equals m.Id
            where roleIds.Contains(g.RoleId) && p.IsActive && p.DeletedAt == null && m.ProductCode == productCode
            select p.Slug
        ).Distinct().OrderBy(s => s).ToListAsync(ct).ConfigureAwait(false);

        return new UserProductGrants(roles.Select(r => (r.Id, r.Code)).ToList(), permissions);
    }
}
