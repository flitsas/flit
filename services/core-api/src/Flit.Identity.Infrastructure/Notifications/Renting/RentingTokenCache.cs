using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>Token de login vigente, con el vencimiento EFECTIVO calculado por TTL de configuración.</summary>
internal sealed record RentingCachedToken(string Value, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Caché del token de login del canal Renting, con anti-estampida (HU #11360 AC1) y renovación por
/// TTL de configuración (AC2/AC3) — el proveedor no informa expiración, así que el vencimiento no
/// se deriva de la respuesta sino de <c>RENTING_API_LOGIN_CACHE_SECONDS_TTL</c>.
/// </summary>
public interface IRentingTokenCache
{
    /// <summary>
    /// Token vigente, logueando solo si hace falta (caché fría o vencida). Diez llamadas
    /// concurrentes con caché fría producen EXACTAMENTE un login (AC1): la primera lo ejecuta, las
    /// demás esperan el semáforo y reutilizan el resultado.
    /// </summary>
    /// <exception cref="RentingAuthenticationException">El login falló (AC6) — nunca token nulo.</exception>
    Task<string> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Invalida <paramref name="rejectedToken"/> si sigue siendo el token cacheado (AC4 — ante un
    /// 401, el llamador invalida ANTES de reintentar). No hace nada si la caché ya cambió (otra
    /// llamada concurrente ya la refrescó): evita que dos 401 simultáneos disparen dos logins.
    /// </summary>
    void Invalidate(string rejectedToken);
}

/// <summary>
/// Implementación con <see cref="SemaphoreSlim"/> y doble chequeo — misma estructura que
/// <c>Quipux.QuipuxTokenCache</c> (el precedente correcto del repo: anti-estampida real). A
/// diferencia de <c>Consultations.Avaluos.FasecoldaTokenCache</c> (el precedente A EVITAR), esta
/// clase NUNCA atrapa el fallo del login y lo convierte en <c>null</c>: deja propagar
/// <see cref="RentingAuthenticationException"/> tal cual (AC6).
/// <para>
/// El reloj es <see cref="TimeProvider"/> inyectado (no <c>DateTimeOffset.UtcNow</c> directo) para
/// que las pruebas de AC2/AC3 puedan adelantar el tiempo sin dormir el TTL real.
/// </para>
/// </summary>
internal sealed class RentingTokenCache(
    IRentingLoginClient loginClient, IOptions<RentingChannelOptions> options, TimeProvider timeProvider)
    : IRentingTokenCache, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RentingCachedToken? _cached;

    public void Dispose() => _gate.Dispose();

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        // Fast-path: token caliente, sin tocar el semáforo (AC2 — no llama al proveedor).
        if (TryRead() is { } fastPath)
            return fastPath.Value;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Doble chequeo DENTRO del lock: mientras esperábamos, otro hilo pudo haber logueado
            // (AC1 — con 10 concurrentes, solo el primero llega hasta aquí sin encontrar caché).
            if (TryRead() is { } cached)
                return cached.Value;

            var login = await loginClient.LoginAsync(cancellationToken);

            var ttlSeconds = Math.Max(1, options.Value.LoginCacheSecondsTtl);
            var entry = new RentingCachedToken(login.Token, timeProvider.GetUtcNow().AddSeconds(ttlSeconds));
            _cached = entry;
            return entry.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate(string rejectedToken)
    {
        var current = _cached;
        if (current is not null && string.Equals(current.Value, rejectedToken, StringComparison.Ordinal))
            _cached = null;
    }

    private RentingCachedToken? TryRead()
    {
        var entry = _cached; // lectura única: referencia inmutable, sin lectura desgarrada.
        if (entry is null)
            return null;

        return entry.ExpiresAtUtc > timeProvider.GetUtcNow() ? entry : null;
    }
}
