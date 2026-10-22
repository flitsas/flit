using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;

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
    private const string ChequeoIdentidad = "conductor_identidad";
    private const string CampoEstado = "person_license_status";
    private const string CampoCategorias = "person_license_categories";
    private const string LicenciaActiva = "ACTIVA";

    public async Task<RespuestaFuente<ResultadoPersonaRunt>> ConsultarAsync(Guid empresa, DocumentoPersona documento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documento);
        var solicitud = new ConsultarConductorRequest
        {
            Opciones = new OpcionesConsulta(),
            Documento = new DocumentoIdentidad { Tipo = documento.Tipo, Numero = documento.Numero },
        };
        var respuesta = await cliente.LlamarAsync(empresa, (c, o) => c.ConsultarConductorAsync(solicitud, o), ct).ConfigureAwait(false);
        if (respuesta.Error is { } errorGrpc)
            return RespuestaFuente<ResultadoPersonaRunt>.Fallo(errorGrpc);

        var resultado = respuesta.Respuesta!.Resultado;
        var servicio = TraductorErroresConsultas.Servicio(resultado?.Proveedor);
        if (TraductorErroresConsultas.Traducir(resultado) is { } error)
            return RespuestaFuente<ResultadoPersonaRunt>.Fallo(error, servicio);

        return RespuestaFuente<ResultadoPersonaRunt>.Exito(Normalizar(resultado!), servicio, null);
    }

    internal static ResultadoPersonaRunt Normalizar(ResultadoConsulta resultado)
    {
        var inscrito = resultado.Chequeos.Any(c => c.Clave == ChequeoIdentidad && c.Estado == EstadoChequeo.Ok);
        var estado = Campo(resultado, CampoEstado);
        var licencias = (Campo(resultado, CampoCategorias) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(categoria => new LicenciaConduccion(categoria.ToUpperInvariant(), LicenciaActiva, null))
            .ToList();
        return new ResultadoPersonaRunt(inscrito, estado, licencias);
    }

    private static string? Campo(ResultadoConsulta resultado, string clave) =>
        resultado.Campos.FirstOrDefault(c => c.Clave == clave && c.HasValorTexto) is { } campo && !string.IsNullOrWhiteSpace(campo.ValorTexto)
            ? campo.ValorTexto.Trim()
            : null;
}
