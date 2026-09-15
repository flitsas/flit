using Flit.Admin.Application.Companies.Branding;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de <see cref="IBrandingTenantLookup"/> (HU #12418) sobre
/// <c>identity.tenants</c>. Lectura directa (sin caché propia): la releen
/// <c>ResolvePublicBrandingHandler</c>/<c>ResolveSessionBrandingHandler</c> antes de resolver marca,
/// para que apagar la clase MARCA_BLANCA (o desactivar/desvincular la cabeza) se refleje sin
/// depender exclusivamente de la caché de <c>ITenantDomainResolver</c>.
/// </summary>
internal sealed class BrandingTenantLookupRepository : IBrandingTenantLookup
{
    private readonly FlitDbContext _context;

    public BrandingTenantLookupRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<BrandingTenantSnapshot?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var row = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.TenantType, t.IsGroupParent, t.IsActive, t.ParentTenantId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new BrandingTenantSnapshot(tenantId, row.TenantType, row.IsGroupParent, row.IsActive, row.ParentTenantId);
    }
}
