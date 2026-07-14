using System.Net.Http.Json;
using System.Text.Json;

namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>
/// Caché en memoria (singleton) del token OAuth2 de Fasecolda. El token dura ~24h
/// (expires_in ≈ 86399); se renueva 60s antes de expirar o ante ausencia. Evita pedir
/// token en cada consulta. Devuelve null si la autenticación falla (el provider lo trata como error).
/// </summary>
internal sealed class FasecoldaTokenCache : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public void Dispose() => _gate.Dispose();

    public async Task<string?> GetTokenAsync(HttpClient apiClient, FasecoldaOptions options, CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
            return _token;

        await _gate.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
                return _token;

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = options.GrantType,
                ["username"] = options.Username,
                ["password"] = options.Password,
            });

            using var resp = await apiClient.PostAsync(options.AuthPath, form, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var payload = await resp.Content.ReadFromJsonAsync<FasecoldaTokenResponse>(JsonOptions, ct);
            if (string.IsNullOrWhiteSpace(payload?.AccessToken))
                return null;

            _token = payload.AccessToken;
            var ttl = Math.Max(30, payload.ExpiresIn - 60);
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttl);
            return _token;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
