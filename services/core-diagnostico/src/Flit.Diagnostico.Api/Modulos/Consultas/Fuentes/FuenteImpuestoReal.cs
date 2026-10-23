using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Impuesto real (HU #13862, F09-C5, F09-C6): <c>ConsultasService.ConsultarImpuestoVehicular</c> de core-consultas por
/// <see cref="ClienteConsultas"/> (token de servicio, deadline y la empresa en <c>x-flit-tenant-id</c>) con la placa y el
/// departamento. Errores:
/// <list type="bullet">
/// <item><c>UNIMPLEMENTED</c> (core-consultas sin C3) → <c>NO_CONECTADO</c>, definitivo y sin reintentos. Se resuelve AQUÍ, no en
/// <see cref="TraductorErroresConsultas"/>, para no cambiar las demás fuentes (allí sigue siendo <c>ESTADO_NO_OK</c>).</item>
/// <item>El resto llega traducido por <see cref="TraductorErroresConsultas"/>: <c>Unavailable</c> → <c>RED</c> y
/// <c>DeadlineExceeded</c> → <c>TIMEOUT</c> (transitorios); <c>FAILED_PRECONDITION</c> (departamento no soportado en el
/// proveedor), <c>InvalidArgument</c> y los demás → <c>ESTADO_NO_OK</c> (definitivo).</item>
/// <item>OK con un chequeo en ERROR (proveedor caído) → <c>HTTP_5XX</c>, transitorio.</item>
/// </list>
/// El proveedor (<c>resultado.proveedor</c>) solo se convierte en <see cref="RespuestaFuente{T}.Servicio"/> del intento; nunca
/// entra en <see cref="ResultadoImpuesto"/> (R-20). core-consultas no informa transacción en este rpc.
/// </summary>
/// <example>
/// <code>
/// var r = await new FuenteImpuestoReal(cliente).ConsultarAsync(empresa, new SolicitudImpuesto("TST301", "05"), ct);
/// </code>
/// </example>
public sealed class FuenteImpuestoReal(ClienteConsultas cliente) : IFuenteImpuesto
{
    /// <summary>Código de fuente en el mensaje de <c>NO_CONECTADO</c>.</summary>
    private const string CodigoFuente = "IMPUESTO_DEPARTAMENTAL";

    public async Task<RespuestaFuente<ResultadoImpuesto>> ConsultarAsync(Guid empresa, SolicitudImpuesto solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var request = new ConsultarImpuestoVehicularRequest
        {
            Opciones = new OpcionesConsulta(),
            Placa = new Placa { Valor = solicitud.Placa },
            DepartamentoCodigo = solicitud.DepartamentoCodigo,
        };
        var respuesta = await cliente.LlamarAsync(empresa, (c, o) => c.ConsultarImpuestoVehicularAsync(request, o), ct).ConfigureAwait(false);
        if (respuesta.Error is { } errorGrpc)
        {
            return RespuestaFuente<ResultadoImpuesto>.Fallo(
                respuesta.Codigo == StatusCode.Unimplemented ? ErroresFuentePersona.NoConectada(CodigoFuente) : errorGrpc);
        }

        var resultado = respuesta.Respuesta!.Resultado;
        var servicio = TraductorErroresConsultas.Servicio(resultado?.Proveedor);
        if (TraductorErroresConsultas.Traducir(resultado) is { } error)
            return RespuestaFuente<ResultadoImpuesto>.Fallo(error, servicio);

        return RespuestaFuente<ResultadoImpuesto>.Exito(ResultadoImpuesto.Normalizar(respuesta.Respuesta, solicitud.DepartamentoCodigo), servicio, null);
    }
}
