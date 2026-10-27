using System.Net;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Clase del fallo de un proveedor HTTP (<see cref="FalloProveedor"/>) para el resolvedor de errores del Diagnóstico. Solo
/// clasifica: el proveedor sigue respondiendo su <c>ProviderUnavailable</c> de siempre (mismo <c>Overall</c>, checks y mensaje).
/// 401/403 → <see cref="ClaseFalloProveedor.Credenciales"/>, 429 → <see cref="ClaseFalloProveedor.Limite"/>, 400/422 →
/// <see cref="ClaseFalloProveedor.Rechazo"/>; cualquier otro no-2xx (5xx incluidos) y un cuerpo ilegible →
/// <see cref="ClaseFalloProveedor.Caida"/>. Regla pura, sin estado.
/// </summary>
/// <example>
/// <code>
/// if (!response.IsSuccessStatusCode)
///     return ProviderUnavailable(ClasificadorFalloHttp.PorEstado(response.StatusCode));
/// </code>
/// </example>
internal static class ClasificadorFalloHttp
{
    /// <summary>Fallo de una respuesta no-2xx (el 404 lo atiende cada proveedor antes).</summary>
    public static FalloProveedor PorEstado(HttpStatusCode estado)
    {
        var codigo = (int)estado;
        var clase = codigo switch
        {
            401 or 403 => ClaseFalloProveedor.Credenciales,
            429 => ClaseFalloProveedor.Limite,
            400 or 422 => ClaseFalloProveedor.Rechazo,
            _ => ClaseFalloProveedor.Caida,
        };
        return new FalloProveedor(clase, codigo);
    }

    /// <summary>Respuesta sin cuerpo legible (vacío, nulo o JSON inválido); <paramref name="estado"/> si se conoce.</summary>
    public static FalloProveedor CuerpoIlegible(HttpStatusCode? estado = null) =>
        new(ClaseFalloProveedor.Caida, estado is { } e ? (int)e : null);

    /// <summary>Error de red (<see cref="HttpRequestException"/>: DNS, conexión, reset).</summary>
    public static FalloProveedor Red() => new(ClaseFalloProveedor.Red);

    /// <summary>Timeout del cliente HTTP (<see cref="TaskCanceledException"/> sin cancelación del llamador).</summary>
    public static FalloProveedor Timeout() => new(ClaseFalloProveedor.Timeout);
}
