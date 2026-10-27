using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Error de una llamada a core-consultas (HU #13791): <see cref="TipoError"/> es un código de la tabla de
/// <see cref="ClasificadorErrores"/>, así que <see cref="PoliticaReintentos"/> (#13737) decide el reintento sin cambios.
/// <see cref="Mensaje"/> es técnico y fijo: nunca copia el detalle del proveedor (puede traer documentos).
/// </summary>
public sealed record ErrorConsulta(string TipoError, string Mensaje)
{
    public ClaseError Clase => ClasificadorErrores.Clasificar(TipoError);

    /// <summary>Verdadero si se reintenta con backoff (<c>TIMEOUT</c>, <c>RED</c>, <c>HTTP_5XX</c>).</summary>
    public bool EsTransitorio => Clase == ClaseError.Transitorio;
}

/// <summary>
/// Traduce lo que devuelve core-consultas a <see cref="ErrorConsulta"/> (HU #13791):
/// <list type="bullet">
/// <item><see cref="RpcException"/>: <c>Unavailable</c> → <c>RED</c> y <c>DeadlineExceeded</c> → <c>TIMEOUT</c> (transitorios);
/// <c>Unauthenticated</c> y <c>PermissionDenied</c> → <c>AUTENTICACION</c>; <c>InvalidArgument</c> y el resto →
/// <c>ESTADO_NO_OK</c> (definitivos).</item>
/// <item><see cref="ErrorProveedor"/> (documentos generados): <c>transitorio</c> → <c>HTTP_5XX</c>; si no, <c>ESTADO_NO_OK</c>.</item>
/// <item><see cref="ResultadoConsulta"/>: un proveedor caído no es error gRPC sino un chequeo en <c>ESTADO_CHEQUEO_ERROR</c>
/// (el proveedor no respondió) → <c>HTTP_5XX</c> transitorio, salvo que el bus traiga <c>motivoFallo</c> (<see cref="ConMotivo"/>);
/// sin resultado → <c>RESPUESTA_INVALIDA</c>.</item>
/// </list>
/// <see cref="Servicio"/> lleva la clave del proveedor (<c>ResultadoConsulta.proveedor</c>) a <see cref="ServicioConsulta"/> del
/// intento auditable. Regla pura, sin estado.
/// </summary>
/// <example>
/// <code>
/// var error = respuesta.Error ?? TraductorErroresConsultas.Traducir(respuesta.Respuesta!.Resultado);
/// if (error is not null) return ResultadoCelda.Error(error.TipoError);
/// </code>
/// </example>
public static class TraductorErroresConsultas
{
    private const string Red = "RED";
    private const string Timeout = "TIMEOUT";
    private const string Http5xx = "HTTP_5XX";
    private const string Autenticacion = "AUTENTICACION";
    private const string EstadoNoOk = "ESTADO_NO_OK";
    private const string RespuestaInvalida = "RESPUESTA_INVALIDA";
    private const string LimiteProveedor = "LIMITE_PROVEEDOR";

    /// <summary>Motivo de <c>motivoFallo</c> que es una credencial del proveedor vencida o sin permiso (error de configuración).</summary>
    public const string MotivoCredenciales = "CREDENCIALES";

    public static ErrorConsulta Traducir(RpcException excepcion)
    {
        ArgumentNullException.ThrowIfNull(excepcion);
        return TraducirCodigo(excepcion.StatusCode);
    }

    /// <summary>
    /// El mismo error a partir del código gRPC solo (ADR-0075): es el <c>grpcStatus</c> que core-consultas devuelve en
    /// <c>consultas.resultado.diagnostico</c> cuando no pudo consultar.
    /// </summary>
    public static ErrorConsulta Traducir(StatusCode codigo) => TraducirCodigo(codigo);

    private static ErrorConsulta TraducirCodigo(StatusCode codigo)
    {
        var tipo = codigo switch
        {
            StatusCode.Unavailable => Red,
            StatusCode.DeadlineExceeded => Timeout,
            StatusCode.Unauthenticated or StatusCode.PermissionDenied => Autenticacion,
            _ => EstadoNoOk,
        };
        return new ErrorConsulta(tipo, $"core-consultas respondió {codigo}.");
    }

    /// <summary>Null si no hay error (campo ausente, o presente sin mensaje ni <c>transitorio</c>).</summary>
    public static ErrorConsulta? Traducir(ErrorProveedor? error)
    {
        if (error is null || (string.IsNullOrEmpty(error.Mensaje) && !error.Transitorio))
            return null;

        return error.Transitorio
            ? new ErrorConsulta(Http5xx, "El proveedor no respondió; se puede reintentar.")
            : new ErrorConsulta(EstadoNoOk, "El proveedor rechazó la solicitud.");
    }

    /// <summary>Null si el resultado tiene veredicto (ningún chequeo en ERROR).</summary>
    public static ErrorConsulta? Traducir(ResultadoConsulta? resultado)
    {
        if (resultado is null)
            return new ErrorConsulta(RespuestaInvalida, "core-consultas respondió sin resultado.");

        var caido = resultado.Chequeos.FirstOrDefault(c => c.Estado == EstadoChequeo.Error);
        return caido is null ? null : new ErrorConsulta(Http5xx, $"El proveedor no respondió (chequeo {caido.Clave}).");
    }

    /// <summary>
    /// Resolvedor de errores (solo Diagnóstico): el <paramref name="error"/> de un proveedor caído (<c>HTTP_5XX</c> por el chequeo en
    /// ERROR) con el <c>motivoFallo</c> que publicó core-consultas: <c>CREDENCIALES</c> → <c>AUTENTICACION</c> (definitivo),
    /// <c>LIMITE</c> → <c>LIMITE_PROVEEDOR</c> (transitorio), <c>RECHAZO</c> → <c>ESTADO_NO_OK</c>, <c>CAIDA</c> → <c>HTTP_5XX</c>,
    /// <c>RED</c> → <c>RED</c>, <c>TIMEOUT</c> → <c>TIMEOUT</c>. Sin motivo, con uno desconocido o con otro error, el mismo
    /// <paramref name="error"/> (lo de hoy).
    /// </summary>
    public static ErrorConsulta? ConMotivo(ErrorConsulta? error, string? motivoFallo)
    {
        if (error is null || !string.Equals(error.TipoError, Http5xx, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(motivoFallo))
            return error;

        return motivoFallo.Trim().ToUpperInvariant() switch
        {
            MotivoCredenciales => new ErrorConsulta(Autenticacion, "Error de configuración del proveedor: credencial rechazada (401/403)."),
            "LIMITE" => new ErrorConsulta(LimiteProveedor, "El proveedor limitó las peticiones (429); se puede reintentar."),
            "RECHAZO" => new ErrorConsulta(EstadoNoOk, "El proveedor rechazó la solicitud (400/422)."),
            "CAIDA" => error,
            "RED" => new ErrorConsulta(Red, "No hubo conexión con el proveedor."),
            "TIMEOUT" => new ErrorConsulta(Timeout, "El proveedor no respondió a tiempo."),
            _ => error,
        };
    }

    /// <summary>
    /// Servicio del intento para la clave del proveedor: completa sin guiones bajos (<c>mercado_libre</c> → MERCADOLIBRE) o su raíz
    /// (<c>kyverum_runt</c> → KYVERUM); desconocido → <see cref="ServicioConsulta.Otro"/>; vacío → null.
    /// </summary>
    public static ServicioConsulta? Servicio(string? proveedor)
    {
        if (string.IsNullOrWhiteSpace(proveedor))
            return null;

        var clave = proveedor.Trim().ToUpperInvariant();
        if (CodigosDeEnum.TryLeer<ServicioConsulta>(clave.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal), out var completo))
            return completo;

        return CodigosDeEnum.TryLeer<ServicioConsulta>(clave.Split('_', 2)[0], out var raiz) ? raiz : ServicioConsulta.Otro;
    }
}
