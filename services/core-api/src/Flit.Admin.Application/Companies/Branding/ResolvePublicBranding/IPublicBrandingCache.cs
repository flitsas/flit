using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.ResolvePublicBranding;

/// <summary>
/// Puerto de caché de 60 s de <c>ResolvePublicBrandingHandler</c> (HU #12418 AC2/AC7). Clave =
/// <c>headTenantId</c> (nunca el host: ADR-0060 D2 hace 1 host → 1 cabeza, y así
/// <see cref="Flit.Admin.Application.Companies.Branding.IBrandingCacheInvalidator"/> invalida sin tener
/// que resolver el host vigente). Implementación en Infrastructure sobre
/// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> — mismo patrón que
/// <c>CachedTenantDomainResolver</c>.
/// </summary>
public interface IPublicBrandingCache
{
    bool TryGet(Guid headTenantId, out BrandIdentity identity);

    void Store(Guid headTenantId, BrandIdentity identity, TimeSpan ttl);

    /// <summary>
    /// Como <see cref="TryGet(Guid, out BrandIdentity)"/>, con el sello de la fila de marca con que se
    /// guardó (<see cref="ResolvePublicBrandingHandler.NoBrandingStamp"/> si no había fila; <c>null</c>
    /// si la caché no guarda sellos, y entonces no se verifica).
    /// </summary>
    bool TryGet(Guid headTenantId, out BrandIdentity identity, out long? stamp)
    {
        stamp = null;
        return TryGet(headTenantId, out identity);
    }

    void Store(Guid headTenantId, BrandIdentity identity, long? stamp, TimeSpan ttl) =>
        Store(headTenantId, identity, ttl);
}
