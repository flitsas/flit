using Flit.Modules.Quipux.Domain.Puertos;

namespace Flit.Infrastructure.Quipux;

/// <summary>Token de Quipux ya emitido, con su vencimiento efectivo (derivado del claim <c>exp</c> del JWT).</summary>
internal sealed record QuipuxToken(string Value, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Caché del token de Quipux con anti-estampida.
///
/// PORQUÉ EXISTE: en FLIT 1.0 (<c>quipuxService.ts</c> <c>getOrRefreshToken</c>, :238-252) el caché era
/// un <c>get</c> seguido de un <c>set</c> sin ningún tipo de exclusión mutua. Con caché frío, N requests
/// concurrentes veían el miss a la vez y disparaban N logins contra Quipux (estampida): trabajo
/// desperdiciado, riesgo de rate-limit y N tokens emitidos de los que solo sobrevivía el último.
///
/// AQUÍ: un <see cref="SemaphoreSlim"/> serializa el refresco — solo un hilo hace login y el resto espera
/// y reutiliza SU token (doble chequeo dentro del lock). El fast-path de lectura NO toma el lock, así que
/// el caso normal (token caliente) no se serializa.
///
/// CONCURRENCIA: la entrada se guarda como una ÚNICA referencia inmutable (<see cref="CacheEntry"/>) y no
/// como campos sueltos, para que la lectura sin lock sea una lectura de referencia atómica y no pueda ver
/// una clave de una configuración y un token de otra (lectura desgarrada).
/// </summary>
internal sealed class QuipuxTokenCache : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CacheEntry? _entry;

    /// <summary>
    /// Libera el semáforo. En producción el caché es un estático de proceso y vive lo que vive la app, así
    /// que nunca se llama; se implementa por corrección (CA1001) y para que las pruebas puedan desecharlo.
    /// </summary>
    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Devuelve el token vigente para <paramref name="cacheKey"/>, haciendo login solo si hace falta.
    /// </summary>
    /// <param name="cacheKey">
    /// Identidad de la configuración (URL de login + usuario + consumidor). Los settings de Quipux viven
    /// en BD y pueden cambiar en caliente: si cambian, cambia la clave y el token viejo deja de usarse
    /// sin necesidad de reiniciar el proceso.
    /// </param>
    /// <param name="staleToken">
    /// Token que el llamador ya comprobó que Quipux rechazó (401), o null en el camino normal. Sirve para
    /// que ante una tormenta de 401 concurrentes solo UNO re-loguee: los demás entran al lock, ven que la
    /// entrada cacheada ya NO es la que ellos vieron podrida y la reutilizan en vez de re-loguear en cadena.
    /// </param>
    /// <param name="login">Fábrica que ejecuta el login real y calcula el vencimiento a partir del JWT.</param>
    public async Task<string> GetOrRefreshAsync(
        string cacheKey,
        string? staleToken,
        Func<CancellationToken, Task<QuipuxToken>> login,
        CancellationToken cancellationToken)
    {
        // Fast-path: token caliente y no venimos de un 401 ⇒ ni tocamos el semáforo.
        if (staleToken is null && TryRead(cacheKey) is { } cached)
            return cached.Value;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Doble chequeo DENTRO del lock: mientras esperábamos, otro hilo pudo haber refrescado.
            // La comparación contra `staleToken` evita reutilizar justo el token que nos dio 401.
            if (TryRead(cacheKey) is { } fresh && !string.Equals(fresh.Value, staleToken, StringComparison.Ordinal))
                return fresh.Value;

            var issued = await login(cancellationToken);

            // BUG 4 de 1.0 (:249-250): `tokenCache.set("authToken", authResponse.token, ...)` cacheaba
            // `undefined` cuando la respuesta venía sin token, y a partir de ahí TODAS las llamadas
            // mandaban `token: undefined` hasta que expiraba el caché. Nunca se cachea un token vacío.
            if (string.IsNullOrWhiteSpace(issued.Value))
                throw new QuipuxException("Quipux devolvió un token vacío en el login.", isTransient: false);

            _entry = new CacheEntry(cacheKey, issued);
            return issued.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Token vigente para esa clave, o null si no hay, es de otra configuración, o ya venció.</summary>
    private QuipuxToken? TryRead(string cacheKey)
    {
        var entry = _entry; // lectura única: ver nota de CONCURRENCIA en el resumen del tipo.
        if (entry is null || !string.Equals(entry.Key, cacheKey, StringComparison.Ordinal))
            return null;
        return entry.Token.ExpiresAtUtc > DateTimeOffset.UtcNow ? entry.Token : null;
    }

    private sealed record CacheEntry(string Key, QuipuxToken Token);
}
