using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// SIMIT real (HU #13793, F05-C1): <c>ConsultasService.ConsultarMultas</c> de core-consultas por <see cref="ClienteConsultas"/>
/// (token de servicio, deadline y la empresa en <c>x-flit-tenant-id</c>). Por placa envía solo <c>placa</c> (sin documento y
/// con <c>tipo_persona</c> sin especificar: core-consultas decide el proveedor con la configuración de la empresa); por persona
/// (HU #13794, <see cref="EjecutorCeldaSimitPersona"/>) envía <c>documento</c> y <c>tipo_persona</c> JURIDICA para NIT o NATURAL en otro caso. Normaliza con
/// <see cref="ResultadoSimit.Normalizar"/> (nunca datos del infractor ni respuesta cruda); un error gRPC o un proveedor caído
/// llegan traducidos por <see cref="TraductorErroresConsultas"/>.
/// </summary>
/// <example>
/// <code>
/// var r = await new FuenteSimitReal(cliente).ConsultarAsync(empresa, SolicitudSimit.PorPlaca("TST102"), ct);
/// </code>
/// </example>
public sealed class FuenteSimitReal(ClienteConsultas cliente) : IFuenteSimit
{
    public async Task<RespuestaFuente<ResultadoSimit>> ConsultarAsync(Guid empresa, SolicitudSimit solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var request = Solicitud(solicitud);
        var respuesta = await cliente.LlamarAsync(empresa, (c, o) => c.ConsultarMultasAsync(request, o), ct).ConfigureAwait(false);
        if (respuesta.Error is { } errorGrpc)
            return RespuestaFuente<ResultadoSimit>.Fallo(errorGrpc);

        var resultado = respuesta.Respuesta!.Resultado;
        var servicio = TraductorErroresConsultas.Servicio(resultado?.Proveedor);
        if (TraductorErroresConsultas.Traducir(resultado) is { } error)
            return RespuestaFuente<ResultadoSimit>.Fallo(error, servicio);

        return RespuestaFuente<ResultadoSimit>.Exito(ResultadoSimit.Normalizar(resultado!), servicio, null);
    }

    /// <summary>La solicitud gRPC: por placa o por documento, nunca los dos.</summary>
    internal static ConsultarMultasRequest Solicitud(SolicitudSimit solicitud)
    {
        var request = new ConsultarMultasRequest { Opciones = new OpcionesConsulta() };
        if (solicitud.Documento is { } documento)
        {
            request.Documento = new DocumentoIdentidad { Tipo = documento.Tipo.Trim().ToUpperInvariant(), Numero = documento.Numero };
            request.TipoPersona = solicitud.EsJuridica ? TipoPersona.Juridica : TipoPersona.Natural;
        }
        else
        {
            request.Placa = new Placa { Valor = solicitud.Placa };
        }

        return request;
    }
}
