using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Avalúo real (HU #13795, F05-C7): <c>ConsultasService.ConsultarAvaluos</c> de core-consultas por <see cref="ClienteConsultas"/>
/// (token de servicio, deadline y la empresa en <c>x-flit-tenant-id</c>). Envía la placa y el VIN que tenga el vehículo (al
/// menos uno) y modelo, cilindraje, combustible y pasajeros (0 o vacío si el RUNT no los trajo: core-consultas los toma como
/// ausentes). core-consultas consulta las fuentes habilitadas para la empresa y elige el valor sugerido; el producto no elige
/// fuente. Normaliza con <see cref="ResultadoAvaluo.Normalizar"/> (sin mensajes del proveedor). Un error gRPC llega traducido
/// por <see cref="TraductorErroresConsultas"/>; todas las fuentes en ERROR sin valor es <c>HTTP_5XX</c> (transitorio), no
/// «sin valor comercial».
/// </summary>
/// <example>
/// <code>
/// var r = await new FuenteAvaluoReal(cliente).ConsultarAsync(empresa, new SolicitudAvaluo("TST201", null, 2020, 1598, "GASOLINA", 5), ct);
/// </code>
/// </example>
public sealed class FuenteAvaluoReal(ClienteConsultas cliente) : IFuenteAvaluo
{
    private const string Http5xx = "HTTP_5XX";

    public async Task<RespuestaFuente<ResultadoAvaluo>> ConsultarAsync(Guid empresa, SolicitudAvaluo solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var request = Solicitud(solicitud);
        var respuesta = await cliente.LlamarAsync(empresa, (c, o) => c.ConsultarAvaluosAsync(request, o), ct).ConfigureAwait(false);
        if (respuesta.Error is { } errorGrpc)
            return RespuestaFuente<ResultadoAvaluo>.Fallo(errorGrpc);

        var resultado = ResultadoAvaluo.Normalizar(respuesta.Respuesta!);
        var servicio = TraductorErroresConsultas.Servicio(resultado.FuentePrincipal ?? (resultado.Fuentes.Count > 0 ? resultado.Fuentes[0].Fuente : null));
        if (resultado.TodasEnError)
            return RespuestaFuente<ResultadoAvaluo>.Fallo(new ErrorConsulta(Http5xx, "Ninguna fuente de avalúo respondió; se puede reintentar."), servicio);

        return RespuestaFuente<ResultadoAvaluo>.Exito(resultado, servicio, null);
    }

    /// <summary>La solicitud gRPC: placa y VIN si los hay y los datos técnicos (0 o vacío si faltan).</summary>
    internal static ConsultarAvaluosRequest Solicitud(SolicitudAvaluo solicitud)
    {
        var request = new ConsultarAvaluosRequest
        {
            Opciones = new OpcionesConsulta(),
            Modelo = solicitud.Modelo ?? 0,
            Cilindraje = solicitud.Cilindraje ?? 0,
            Combustible = solicitud.Combustible ?? string.Empty,
            Pasajeros = solicitud.Pasajeros ?? 0,
        };
        if (!string.IsNullOrWhiteSpace(solicitud.Placa))
            request.Placa = new Placa { Valor = solicitud.Placa };
        if (!string.IsNullOrWhiteSpace(solicitud.Vin))
            request.Vin = new Vin { Valor = solicitud.Vin };
        return request;
    }
}
