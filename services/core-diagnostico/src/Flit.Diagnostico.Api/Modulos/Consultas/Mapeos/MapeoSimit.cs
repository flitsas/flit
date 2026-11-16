using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;

/// <summary>
/// Parte de respuesta de SIMIT (ADR-0075; antes en <c>FuenteSimitReal</c>, HU #13793, F05-C1): normaliza <c>ConsultarMultas</c>
/// con <see cref="ResultadoSimit.Normalizar"/> (semáforo y comparendos; nunca datos del infractor ni la respuesta cruda). El
/// proveedor caído, la respuesta sin resultado y los errores gRPC llegan traducidos por <see cref="TraductorErroresConsultas"/>.
/// </summary>
/// <example>
/// <code>
/// var r = MapeoSimit.Mapear(respuesta); // RespuestaFuente&lt;ResultadoSimit&gt;
/// </code>
/// </example>
public static class MapeoSimit
{
    public static RespuestaFuente<ResultadoSimit> Mapear(ConsultarMultasResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var resultado = respuesta.Resultado;
        var servicio = TraductorErroresConsultas.Servicio(resultado?.Proveedor);
        if (TraductorErroresConsultas.Traducir(resultado) is { } error)
            return RespuestaFuente<ResultadoSimit>.Fallo(error, servicio);

        return RespuestaFuente<ResultadoSimit>.Exito(ResultadoSimit.Normalizar(resultado!), servicio, null);
    }

    public static RespuestaFuente<ResultadoSimit> Error(StatusCode codigo) =>
        RespuestaFuente<ResultadoSimit>.Fallo(TraductorErroresConsultas.Traducir(codigo));
}
