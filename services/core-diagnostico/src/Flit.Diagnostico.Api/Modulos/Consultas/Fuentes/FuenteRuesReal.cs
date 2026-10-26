using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// RUES real (HU #13792, F05-C9, decisión Q2): <c>ConsultasService.ConsultarRues</c> de core-consultas (proveedor
/// <c>verifik_rues</c>) por <see cref="ClienteConsultas"/>, con el NIT sin dígito de verificación. Normaliza a
/// <see cref="ResultadoRues"/> leyendo SOLO el estado y la fecha de renovación: de la primera matrícula mercantil certificada
/// (<c>estado</c>, <c>renovada_en</c>) y, si no viene, de los campos <c>rues_estado</c>, <c>rues_fecha_renovacion</c> o
/// <c>rues_ultimo_ano_renovado</c>. Nunca lee la razón social, el NIT, la dirección ni los representantes.
/// </summary>
public sealed class FuenteRuesReal(ClienteConsultas cliente) : IFuenteRues
{
    public async Task<RespuestaFuente<ResultadoRues>> ConsultarAsync(Guid empresa, string nit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nit);
        var solicitud = new ConsultarRuesRequest { Opciones = new OpcionesConsulta(), Nit = nit };
        var respuesta = await cliente.LlamarAsync(empresa, (c, o) => c.ConsultarRuesAsync(solicitud, o), ct).ConfigureAwait(false);
        return respuesta.Respuesta is { } r ? MapeoRues.Mapear(r) : MapeoRues.Error(respuesta.Codigo ?? StatusCode.Unknown);
    }
}
