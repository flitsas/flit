using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Gateway.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Gateway.Cors;

/// <summary>
/// Orígenes CORS admitidos (HU #12417 AC3/AC4, ADR-0060 D2): la lista FIJA de configuración
/// (<c>Cors:AllowedOrigins</c>) unida a <c>https://{host}</c> de cada dominio ACTIVO
/// (<c>GET /api/v1/internal/domains/active</c> en <c>Flit.Api</c>), con caché de 60 s y respaldo a
/// la ÚLTIMA lista buena si la API interna no responde — CORS nunca queda vacío por un fallo
/// transitorio (AC4). Se consulta por petición vía <c>SetIsOriginAllowed</c> (Program.cs), nunca
/// con <c>WithOrigins</c> estático: dar de alta un dominio se refleja sin redespliegue.
/// </summary>
public sealed class DynamicCorsOriginSource(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<InternalApiOptions> internalOptions,
    IMemoryCache cache,
    ILogger<DynamicCorsOriginSource> logger)
{
    /// <summary>Nombre del <see cref="IHttpClientFactory"/> con el que se registra el cliente HTTP dedicado (Program.cs).</summary>
    public const string HttpClientName = nameof(DynamicCorsOriginSource);

    private const string CacheKey = "flit:gateway:cors:dynamic-origins";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IOptionsMonitor<InternalApiOptions> _internalOptions =
        internalOptions ?? throw new ArgumentNullException(nameof(internalOptions));
    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly ILogger<DynamicCorsOriginSource> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Última lista buena — respaldo si la API interna no responde ni para renovar el caché (AC4).
    private volatile IReadOnlyList<string> _lastGoodOrigins = [];

    public async Task<bool> IsOriginAllowedAsync(
        string origin,
        IReadOnlyList<string> fixedOrigins,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixedOrigins);

        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        if (fixedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var dynamicOrigins = await GetDynamicOriginsAsync(cancellationToken).ConfigureAwait(false);
        return dynamicOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<string>> GetDynamicOriginsAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<IReadOnlyList<string>>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var options = _internalOptions.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            // Sin clave configurada: nunca se llama a la API interna — fail-closed a la lista fija
            // (o a la última lista buena, si alguna vez la hubo).
            return _lastGoodOrigins;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/internal/domains/active");
            request.Headers.TryAddWithoutValidation("X-Internal-Key", options.ApiKey);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content
                .ReadFromJsonAsync(ActiveDomainsJsonContext.Default.ActiveDomainsResponse, cancellationToken)
                .ConfigureAwait(false);
            var hosts = payload?.Hosts ?? [];

            IReadOnlyList<string> origins = hosts
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => $"https://{h.Trim().ToLowerInvariant()}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _lastGoodOrigins = origins;
            _cache.Set(CacheKey, origins, Ttl);
            return origins;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DynamicCorsLog.FetchFailed(_logger, ex);
            // Fallo transitorio (AC4): se conserva la ÚLTIMA lista buena y NO se cachea el fallo —
            // así la siguiente petición reintenta en vez de arrastrar el error 60 s.
            return _lastGoodOrigins;
        }
    }

    internal sealed record ActiveDomainsResponse(IReadOnlyList<string>? Hosts);
}

/// <summary>Contexto JSON source-generado (AOT/trimming) para deserializar <c>GET /internal/domains/active</c>.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DynamicCorsOriginSource.ActiveDomainsResponse))]
internal sealed partial class ActiveDomainsJsonContext : JsonSerializerContext;

/// <summary>Logging source-generado (CA1848) de <see cref="DynamicCorsOriginSource"/>.</summary>
internal static partial class DynamicCorsLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Fallo al obtener dominios activos para CORS; se usa la última lista buena.")]
    public static partial void FetchFailed(ILogger logger, Exception ex);
}
