using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.ResolvePublicBranding;
using Flit.Admin.Domain.Companies.Branding;
using Microsoft.Extensions.Caching.Memory;

namespace Flit.Infrastructure.Domains;

/// <summary>
/// Caché de 60 s de <c>ResolvePublicBrandingHandler</c> + invalidación explícita (HU #12418
/// AC2/AC7, ADR-0060 D2). Implementa AMBOS puertos (<see cref="IPublicBrandingCache"/> y
/// <see cref="IBrandingCacheInvalidator"/>) sobre el MISMO <see cref="IMemoryCache"/> singleton, así
/// que <c>InvalidateTenant</c> borra exactamente lo que escribió <c>Set</c>. Clave = <c>tenantId</c>
/// de la cabeza (no el host): un host resuelve siempre a la misma cabeza (ADR-0060 D2), así que
/// publicar/retirar/cambiar de clase invalida sin tener que buscar el host vigente.
/// </summary>
internal sealed class MemoryPublicBrandingCache : IPublicBrandingCache, IBrandingCacheInvalidator
{
    private const string KeyPrefix = "flit:public-branding:tenant:";

    private readonly IMemoryCache _cache;

    public MemoryPublicBrandingCache(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public bool TryGet(Guid headTenantId, out BrandIdentity identity)
    {
        if (_cache.TryGetValue<BrandIdentity>(KeyFor(headTenantId), out var cached) && cached is not null)
        {
            identity = cached;
            return true;
        }

        identity = BrandIdentity.Flit;
        return false;
    }

    public void Store(Guid headTenantId, BrandIdentity identity, TimeSpan ttl) =>
        _cache.Set(KeyFor(headTenantId), identity, ttl);

    public void InvalidateTenant(Guid tenantId)
    {
        _cache.Remove(KeyFor(tenantId));

        // HU #12428 AC5 — mismo evento que invalida la identidad pública/sesión (publicar/retirar
        // marca, cambio de tenant_type) también debe invalidar el tema de correo cacheado por
        // DbEmailThemeResolver: comparten el mismo IMemoryCache singleton, así que basta con limpiar
        // también su clave aquí, sin una segunda suscripción a los mismos eventos.
        _cache.Remove(Notifications.Theme.DbEmailThemeResolver.CacheKeyFor(tenantId));
    }

    private static string KeyFor(Guid tenantId) => KeyPrefix + tenantId;
}
