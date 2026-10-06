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
    private string? _token;
    private DateTimeOffset _expires = DateTimeOffset.MinValue;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (Current() is { } cached)
            return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Current() is { } renewed)
                return renewed;

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _opts.ClientId,
                ["client_secret"] = _opts.ClientSecret,
                ["scope"] = _opts.IdentityScope,
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

            _token = body.AccessToken;
            _expires = time.GetUtcNow().AddSeconds(body.ExpiresIn > 0 ? body.ExpiresIn : 300);
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private string? Current() => _token is not null && time.GetUtcNow() < _expires - RenewBefore ? _token : null;

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
