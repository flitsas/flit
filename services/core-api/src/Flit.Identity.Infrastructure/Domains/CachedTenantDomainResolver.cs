using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Domain.Companies.Domains;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Domains;

/// <summary>
/// Resolutor de red por dominio con caché de 60 s (HU #12416 AC4, ADR-0060 D2). Lee EXCLUSIVAMENTE
/// <c>admin.v_active_network_domains</c> (vía <see cref="ITenantDomainRepository.FindActiveHeadTenantIdAsync"/>)
/// — nunca <c>tenant_domains</c> sin filtro de estado, así apagar la clase MARCA_BLANCA o inactivar la
/// cabeza saca el host de la vista y este resolutor deja de encontrarlo sin código adicional aquí.
/// Entrada NEGATIVA cacheada igual que la positiva (mismo costo/tiempo para host desconocido, defensa
/// anti-enumeración transversal a la épica) y CUALQUIER fallo se trata y cachea como <see cref="NetworkResolution.None"/>
/// con log — nunca una red distinta (AC4, última cláusula). <see cref="IMemoryCache"/> es Singleton en
/// el contenedor; esta clase es Scoped (una <c>FlitDbContext</c> por resolución), el caché se comparte
/// entre peticiones igual.
/// </summary>
internal sealed class CachedTenantDomainResolver(
    ITenantDomainRepository repository,
    IMemoryCache cache,
    ILogger<CachedTenantDomainResolver> logger) : ITenantDomainResolver
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private const string ResolveCacheKeyPrefix = "flit:domain-resolve:";
    internal const string ActiveHostsCacheKey = "flit:domain-resolve:active-hosts";

    /// <summary>
    /// Clave de caché de un host (visible para <c>TenantDomainRepository</c>, HU #12425 AC3): al
    /// activar o fallar un dominio, la transición invalida su entrada aquí y en
    /// <see cref="ActiveHostsCacheKey"/> — sin esperar el TTL de 60 s — porque "un dominio que no está
    /// activo no resuelve marca ni permite autenticar" debe verse de inmediato, no en hasta un minuto.
    /// </summary>
    internal static string ResolveCacheKey(string normalizedHost) => ResolveCacheKeyPrefix + normalizedHost;

    private readonly ITenantDomainRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly ILogger<CachedTenantDomainResolver> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<NetworkResolution> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var normalizedHost = host.Trim().ToLowerInvariant();
        var cacheKey = ResolveCacheKey(normalizedHost);

        if (_cache.TryGetValue<NetworkResolution>(cacheKey, out var cached))
        {
            return cached;
        }

        NetworkResolution resolution;
        try
        {
            var headTenantId = await _repository.FindActiveHeadTenantIdAsync(normalizedHost, cancellationToken).ConfigureAwait(false);
            if (headTenantId is { } id)
            {
                // HU #12968: HUB (o sin dato) es la plataforma de la red; si no, el producto del dominio.
                var purpose = await _repository.FindActivePurposeAsync(normalizedHost, cancellationToken).ConfigureAwait(false);
                resolution = NetworkResolution.Head(id, purpose is null or TenantDomainPurposes.Hub ? "plataforma" : purpose);
            }
            else
            {
                resolution = NetworkResolution.None;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-closed (AC4): un fallo del resolutor nunca produce una red — siempre "sin red".
            DomainResolverLog.ResolveFailed(_logger, normalizedHost, ex);
            resolution = NetworkResolution.None;
        }

        _cache.Set(cacheKey, resolution, Ttl);
        return resolution;
    }

    public async Task<IReadOnlyList<string>> ListActiveHostsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue<IReadOnlyList<string>>(ActiveHostsCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        IReadOnlyList<string> hosts;
        try
        {
            hosts = await _repository.ListActiveHostsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DomainResolverLog.ListActiveHostsFailed(_logger, ex);
            hosts = [];
        }

        _cache.Set(ActiveHostsCacheKey, hosts, Ttl);
        return hosts;
    }
}

/// <summary>Logging source-generado (CA1848) de <see cref="CachedTenantDomainResolver"/>.</summary>
internal static partial class DomainResolverLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Fallo al resolver el dominio {Host}; se trata como sin red (HU #12416 AC4).")]
    public static partial void ResolveFailed(ILogger logger, string host, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Fallo al listar dominios activos para CORS; se devuelve lista vacía.")]
    public static partial void ListActiveHostsFailed(ILogger logger, Exception ex);
}
