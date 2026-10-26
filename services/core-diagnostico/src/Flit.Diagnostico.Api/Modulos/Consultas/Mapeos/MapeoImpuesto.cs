using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;

/// <summary>
/// Parte de respuesta del impuesto departamental (ADR-0075; antes en <c>FuenteImpuestoReal</c>, HU #13862, F09-C5, F09-C6):
/// normaliza <c>ConsultarImpuestoVehicular</c> con <see cref="ResultadoImpuesto.Normalizar"/> y el departamento consultado; el
/// proveedor solo va al servicio del intento (R-20). Errores: <c>UNIMPLEMENTED</c> (core-consultas sin C3) es
/// <c>NO_CONECTADO</c>, definitivo; el resto pasa por <see cref="TraductorErroresConsultas"/> (<c>FAILED_PRECONDITION</c>,
/// departamento no soportado en el proveedor, es <c>ESTADO_NO_OK</c>); un chequeo en ERROR es <c>HTTP_5XX</c>, transitorio.
/// </summary>
/// <example>
/// <code>
/// var r = MapeoImpuesto.Mapear(respuesta, "05"); // RespuestaFuente&lt;ResultadoImpuesto&gt;
/// </code>
/// </example>
public static class MapeoImpuesto
{
    /// <summary>Código de fuente en el mensaje de <c>NO_CONECTADO</c>.</summary>
    public const string CodigoFuente = "IMPUESTO_DEPARTAMENTAL";

    public static RespuestaFuente<ResultadoImpuesto> Mapear(ConsultarImpuestoVehicularResponse respuesta, string departamentoCodigo)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var resultado = respuesta.Resultado;
        var servicio = TraductorErroresConsultas.Servicio(resultado?.Proveedor);
        if (TraductorErroresConsultas.Traducir(resultado) is { } error)
            return RespuestaFuente<ResultadoImpuesto>.Fallo(error, servicio);

        return RespuestaFuente<ResultadoImpuesto>.Exito(ResultadoImpuesto.Normalizar(respuesta, departamentoCodigo), servicio, null);
    }

    public static RespuestaFuente<ResultadoImpuesto> Error(StatusCode codigo) =>
        RespuestaFuente<ResultadoImpuesto>.Fallo(
            codigo == StatusCode.Unimplemented ? ErroresFuentePersona.NoConectada(CodigoFuente) : TraductorErroresConsultas.Traducir(codigo));
}
