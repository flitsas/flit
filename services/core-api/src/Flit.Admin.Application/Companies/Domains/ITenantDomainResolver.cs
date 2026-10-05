namespace Flit.Admin.Application.Companies.Domains;

/// <summary>
/// Resultado de resolver un host a una red (ADR-0060 D2, HU #12416 AC4). <see cref="None"/> es "sin
/// red" — equivale al dominio de FLIT — para host desconocido, pendiente, fallido, de una cabeza
/// inactiva o ante cualquier fallo del resolutor (fail-closed, nunca una red distinta).
/// </summary>
/// <remarks>
/// HU #12968: <see cref="ProductCode"/> es el producto del dominio (<c>admin.tenant_domains.purpose</c>):
/// <c>plataforma</c> para el dominio <c>HUB</c> de la red, o el código del producto.
/// </remarks>
public readonly record struct NetworkResolution(bool IsNetwork, Guid? HeadTenantId, string ProductCode = "plataforma")
{
    public static NetworkResolution None { get; } = new(false, null);

    public static NetworkResolution Head(Guid headTenantId, string productCode = "plataforma") => new(true, headTenantId, productCode);
}

/// <summary>
/// Punto único de resolución de red por dominio (HU #12416 AC4, ADR-0060 D2). Implementación con
/// caché de 60 s (incluida la resolución negativa) en
/// <c>Flit.Infrastructure.Domains.CachedTenantDomainResolver</c>. Lo consumen el
/// <c>DomainContextMiddleware</c> de #12417 (vía sello <c>X-Flit-Domain</c>, nunca un parámetro del
/// cliente) y la resolución pública de marca de #12418.
/// </summary>
public interface ITenantDomainResolver
{
    /// <summary><paramref name="host"/> debe llegar ya normalizado (minúsculas, punycode).</summary>
    Task<NetworkResolution> ResolveAsync(string host, CancellationToken cancellationToken = default);

    /// <summary>Hosts <c>active</c> de toda la plataforma — orígenes CORS dinámicos del Gateway (#12417).</summary>
    Task<IReadOnlyList<string>> ListActiveHostsAsync(CancellationToken cancellationToken = default);
}
