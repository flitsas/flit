using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;

/// <summary>
/// Parte de respuesta del avalúo (ADR-0075; antes en <c>FuenteAvaluoReal</c>, HU #13795, F05-C7): normaliza
/// <c>ConsultarAvaluos</c> con <see cref="ResultadoAvaluo.Normalizar"/> (sin mensajes del proveedor). Todas las fuentes en ERROR
/// sin valor es <c>HTTP_5XX</c> (transitorio), no «sin valor comercial». El servicio del intento es la fuente principal o, si
/// no hay, la primera fuente. Los errores gRPC llegan traducidos por <see cref="TraductorErroresConsultas"/>.
/// </summary>
/// <example>
/// <code>
/// var r = MapeoAvaluo.Mapear(respuesta); // RespuestaFuente&lt;ResultadoAvaluo&gt;
/// </code>
/// </example>
public static class MapeoAvaluo
{
    private const string Http5xx = "HTTP_5XX";

    public static RespuestaFuente<ResultadoAvaluo> Mapear(ConsultarAvaluosResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var resultado = ResultadoAvaluo.Normalizar(respuesta);
        var servicio = TraductorErroresConsultas.Servicio(resultado.FuentePrincipal ?? (resultado.Fuentes.Count > 0 ? resultado.Fuentes[0].Fuente : null));
        if (resultado.TodasEnError)
            return RespuestaFuente<ResultadoAvaluo>.Fallo(new ErrorConsulta(Http5xx, "Ninguna fuente de avalúo respondió; se puede reintentar."), servicio);

        return RespuestaFuente<ResultadoAvaluo>.Exito(resultado, servicio, null);
    }

    public static RespuestaFuente<ResultadoAvaluo> Error(StatusCode codigo) =>
        RespuestaFuente<ResultadoAvaluo>.Fallo(TraductorErroresConsultas.Traducir(codigo));
}
