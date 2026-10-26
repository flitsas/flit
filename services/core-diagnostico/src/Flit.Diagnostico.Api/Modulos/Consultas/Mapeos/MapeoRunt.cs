using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Grpc.Core;
using TipoErrorFuente = Flit.Diagnostico.Api.Modulos.Consultas.Fuentes.TipoErrorFuente;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;

/// <summary>
/// Parte de respuesta de la RUNT del vehículo (ADR-0075, HU #13727; antes en <c>FuenteRuntReal</c>, HU #13878): la respuesta de
/// <c>RuntIntempoService</c> pasa por <see cref="MapperIntempo"/> con <see cref="Servicio"/>; un error gRPC se traduce aquí:
/// <c>FAILED_PRECONDITION</c> (Intempo sin configurar en core-consultas) y <c>UNIMPLEMENTED</c> son
/// <see cref="TipoErrorFuente.NoConectado"/>, definitivo; el resto pasa por <see cref="TraductorErroresConsultas"/>
/// (<c>Unavailable</c> → <c>RED</c> y <c>DeadlineExceeded</c> → <c>TIMEOUT</c>, transitorios). Mensajes fijos: nunca la placa,
/// el VIN, documentos ni el detalle del servidor. Puro y sin logs.
/// </summary>
/// <example>
/// <code>
/// var runt = estado == "OK" ? MapeoRunt.Mapear(respuesta) : MapeoRunt.Error(StatusCode.Unavailable); // RED, transitorio
/// </code>
/// </example>
public static class MapeoRunt
{
    /// <summary>Servicio que resolvió (<c>EjecutorCeldaRunt</c> lo lleva a <c>ServicioConsulta.Intempo</c>).</summary>
    public const string Servicio = "INTEMPO";

    /// <summary>Mensaje de la RUNT no conectada en core-consultas (sin Intempo configurado o sin el rpc).</summary>
    public const string MensajeNoConectado = "La fuente RUNT no está conectada en este ambiente.";

    /// <summary>La respuesta OK de <c>ConsultarPorPlaca</c> o <c>ConsultarPorVin</c>.</summary>
    public static ResultadoRunt Mapear(ConsultarVehiculoIntempoResponse respuesta) => MapperIntempo.Mapear(respuesta, Servicio);

    /// <summary>El error gRPC con que core-consultas no pudo consultar.</summary>
    public static ResultadoRunt Error(StatusCode codigo)
    {
        if (codigo is StatusCode.FailedPrecondition or StatusCode.Unimplemented)
            return ResultadoRunt.Fallo(TipoErrorFuente.NoConectado, MensajeNoConectado);

        var error = TraductorErroresConsultas.Traducir(codigo);
        return ResultadoRunt.Fallo(Tipo(error.TipoError), error.Mensaje);
    }

    /// <summary>Código de <see cref="TraductorErroresConsultas"/> → <see cref="TipoErrorFuente"/>; desconocido → definitivo.</summary>
    private static TipoErrorFuente Tipo(string codigo) =>
        Enum.GetValues<TipoErrorFuente>().FirstOrDefault(t => TipoErrorFuenteCodigos.Codigo(t) == codigo, TipoErrorFuente.EstadoNoOk);
}
