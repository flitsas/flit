using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Flit.Impuestos.Api.Modulos.ApiLiquidacion;

/// <summary>
/// Traduce lo que pasó en una llamada al API de liquidación a <see cref="ClaseEnvio"/> (plan §5.6, D-ALTA-REINTENTOS):
/// <list type="bullet">
///   <item><see cref="ClaseEnvio.NoSalio"/>: <c>SocketException</c> <c>ConnectionRefused</c> o <c>HostNotFound</c>, error de
///   resolución de nombre, o <c>AuthenticationException</c> del handshake TLS. Solo esto admite reintento automático.</item>
///   <item><see cref="ClaseEnvio.Incierto"/>: timeout (<c>TaskCanceledException</c> sin que el llamador cancele),
///   <c>HttpIOException</c> u otro corte tras enviar, cualquier otra excepción y todo 5xx.</item>
///   <item><see cref="ClaseEnvio.Respuesta"/>: el resto de estados, con el <c>error.code</c> del cuerpo.</item>
/// </list>
/// Si el que canceló fue el llamador, la cancelación sigue su curso (no es un resultado). Sin Polly ni dependencias nuevas.
/// </summary>
/// <example>
/// <code>
/// catch (Exception ex) when (ClasificadorEnvio.Clasificable(ex))
/// {
///     return ClasificadorEnvio.DeExcepcion&lt;FuentesRespuesta&gt;(ex, ct);
/// }
/// </code>
/// </example>
public static class ClasificadorEnvio
{
    /// <summary>Excepciones que el cliente clasifica en lugar de dejarlas subir.</summary>
    public static bool Clasificable(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex is HttpRequestException or OperationCanceledException or IOException or SocketException or AuthenticationException;
    }

    /// <summary>Hay certeza de que la petición no salió.</summary>
    public static bool NoSalio(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (ex is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError })
            return true;
        for (Exception? actual = ex; actual is not null; actual = actual.InnerException)
        {
            switch (actual)
            {
                case SocketException { SocketErrorCode: SocketError.ConnectionRefused or SocketError.HostNotFound }:
                case AuthenticationException:
                    return true;
                case HttpIOException:
                    return false;
            }
        }

        return false;
    }

    /// <summary>Clasifica una excepción de la llamada; relanza la cancelación si la pidió el llamador.</summary>
    public static ResultadoEnvio<T> DeExcepcion<T>(Exception ex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
            throw new OperationCanceledException("La llamada al API de liquidación se canceló.", ex, ct);
        if (NoSalio(ex))
            return ResultadoEnvio<T>.NoSalio($"no salió ({Nombre(ex)})");
        return ex is OperationCanceledException
            ? ResultadoEnvio<T>.Incierto("timeout esperando la respuesta")
            : ResultadoEnvio<T>.Incierto($"corte o error tras enviar ({Nombre(ex)})");
    }

    /// <summary>Clasifica una respuesta que no es 2xx: 5xx es incierto; lo demás, respuesta con su <c>error.code</c>.</summary>
    public static ResultadoEnvio<T> DeEstado<T>(HttpStatusCode status, string? cuerpoError)
    {
        var codigo = ErroresApi.LeerCodigo(cuerpoError);
        var numero = (int)status;
        return numero >= 500
            ? ResultadoEnvio<T>.Incierto($"el API respondió {numero}", numero, codigo)
            : ResultadoEnvio<T>.Respuesta(numero, codigo);
    }

    /// <summary>Tipo de la excepción más interna, para el motivo (sin su mensaje, que puede traer la URL o datos).</summary>
    private static string Nombre(Exception ex)
    {
        var interna = ex;
        while (interna.InnerException is not null)
            interna = interna.InnerException;
        return interna is SocketException s ? $"{interna.GetType().Name} {s.SocketErrorCode}" : interna.GetType().Name;
    }
}
