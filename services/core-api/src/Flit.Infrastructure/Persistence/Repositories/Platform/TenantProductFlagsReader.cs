using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories.Platform;

/// <summary>
/// <see cref="ITenantProductFlags"/> sobre <c>platform.tenant_products</c> (HU #12967, B-07). Lee la fila de
/// la empresa, sin aplicar la regla de la cabeza: la configuración muestra lo que tiene la empresa; el acceso
/// efectivo lo decide el resolutor (B-05).
/// </summary>
internal sealed class TenantProductFlagsReader(FlitDbContext context) : ITenantProductFlags
{
    public async Task<TenantProductFlags> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var enabled = await context.Set<TenantProductEntity>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Enabled && (r.ProductCode == "tramites" || r.ProductCode == "comparendos"))
            .Select(r => r.ProductCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new TenantProductFlags(enabled.Contains("tramites"), enabled.Contains("comparendos"));
    }
}
