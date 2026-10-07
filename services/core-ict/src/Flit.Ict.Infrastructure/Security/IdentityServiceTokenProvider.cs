using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Security;

/// <summary>
/// Token de servicio de core-ict emitido por Identidad (Epic #13316, HU #13335): client credentials con el cliente
/// <c>svc-ict</c> y el scope <c>platform.tramites.ict</c>, que Identidad emite con <c>aud=tramites</c>. Se guarda hasta
/// un minuto antes de vencer; varias llamadas a la vez piden uno solo. Si Identidad no responde, la llamada gRPC falla
/// (no sale sin token) y el job la reintenta en su siguiente ciclo, como cualquier caída de core-api.
/// </summary>
public sealed class IdentityServiceTokenProvider(IHttpClientFactory httpClients, IOptions<IctServiceTokenOptions> options, TimeProvider time)
    : IDisposable
{
    public const string HttpClientName = "identity-service-token";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);

    private readonly IctServiceTokenOptions _opts = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _cache = new(StringComparer.Ordinal);

    /// <summary>Token para el scope de ICT hacia core-api (<see cref="IctServiceTokenOptions.IdentityScope"/>).</summary>
    public Task<string> GetTokenAsync(CancellationToken ct) => GetTokenAsync(_opts.IdentityScope, ct);

    /// <summary>
    /// Token para <paramref name="scope"/>: cada scope lleva la audiencia de su servicio destino (HU #13346:
    /// <c>platform.consultas</c> para core-consultas), así que se guarda uno por scope.
    /// </summary>
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
            {
                throw new InvalidOperationException(
                    $"Identidad respondió {(int)response.StatusCode} al pedir el token de servicio de {_opts.ClientId}.");
            }

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
