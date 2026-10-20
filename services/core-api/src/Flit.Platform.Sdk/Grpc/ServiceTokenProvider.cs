using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Flit.Platform.Sdk.Grpc;

/// <summary>Cliente de servicio con el que este servicio pide sus tokens a Identidad (sección <c>Platform:ServiceClient</c>).</summary>
public sealed class PlatformServiceClientOptions
{
    public const string SectionName = "Platform:ServiceClient";

    /// <summary>Endpoint de token por la red interna (p. ej. <c>http://gateway:4002/connect/token</c>).</summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary><c>svc-&lt;código&gt;</c> (contrato v1.3 §3).</summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Tokens de servicio por client credentials (HU #13337): uno por scope, porque cada scope lleva la audiencia del
/// servicio destino (contrato v1.3 §3). Se guardan hasta un minuto antes de vencer; varias llamadas a la vez piden uno
/// solo. Si Identidad no responde, la llamada falla: nunca sale una llamada sin token.
/// </summary>
public sealed class ServiceTokenProvider(IHttpClientFactory httpClients, IOptions<PlatformServiceClientOptions> options, TimeProvider time)
    : IDisposable
{
    public const string HttpClientName = "flit-platform-service-token";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);

    private readonly PlatformServiceClientOptions _opts = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _cache = new(StringComparer.Ordinal);

    public async Task<string> GetTokenAsync(string scope, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (Current(scope) is { } cached)
            return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Current(scope) is { } renewed)
                return renewed;

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _opts.ClientId,
                ["client_secret"] = _opts.ClientSecret,
                ["scope"] = scope,
            });
            using var client = httpClients.CreateClient(HttpClientName);
            using var response = await client.PostAsync(new Uri(_opts.TokenEndpoint), form, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Identidad respondió {(int)response.StatusCode} al pedir el token de {_opts.ClientId} para {scope}.");

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(body?.AccessToken))
                throw new InvalidOperationException($"Identidad no devolvió access_token para {_opts.ClientId}.");

            _cache[scope] = (body.AccessToken, time.GetUtcNow().AddSeconds(body.ExpiresIn > 0 ? body.ExpiresIn : 300));
            return body.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private string? Current(string scope) =>
        _cache.TryGetValue(scope, out var entry) && time.GetUtcNow() < entry.Expires - RenewBefore ? entry.Token : null;

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
