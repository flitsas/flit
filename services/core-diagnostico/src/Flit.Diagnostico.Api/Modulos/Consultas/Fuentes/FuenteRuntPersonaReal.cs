using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Platform.Grpc.V1;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// RUNT persona real (HU #13792, F05-C4): <c>ConsultasService.ConsultarConductor</c> de core-consultas por
/// <see cref="ClienteConsultas"/> (token de servicio, deadline y la empresa en <c>x-flit-tenant-id</c>). Normaliza
/// <see cref="ResultadoConsulta"/> a <see cref="ResultadoPersonaRunt"/> leyendo SOLO: el chequeo <c>conductor_identidad</c>
/// (inscrito si está OK), <c>person_license_status</c> y <c>person_license_categories</c> (las categorías de las licencias
/// activas). Nunca lee los campos de nombres (<c>person_full_name</c>, <c>person_first_name</c>…) ni la respuesta cruda. Un
/// error gRPC o un proveedor caído llegan traducidos por <see cref="TraductorErroresConsultas"/>.
/// </summary>
public sealed class FuenteRuntPersonaReal(ClienteConsultas cliente) : IFuenteRuntPersona
{
    public async Task<RespuestaFuente<ResultadoPersonaRunt>> ConsultarAsync(Guid empresa, DocumentoPersona documento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documento);
        var solicitud = new ConsultarConductorRequest
        {
            Opciones = new OpcionesConsulta(),
            Documento = new DocumentoIdentidad { Tipo = documento.Tipo, Numero = documento.Numero },
        };
        var respuesta = await cliente.LlamarAsync(empresa, (c, o) => c.ConsultarConductorAsync(solicitud, o), ct).ConfigureAwait(false);
        return respuesta.Respuesta is { } r ? MapeoRuntPersona.Mapear(r) : MapeoRuntPersona.Error(respuesta.Codigo ?? StatusCode.Unknown);
    }
}
