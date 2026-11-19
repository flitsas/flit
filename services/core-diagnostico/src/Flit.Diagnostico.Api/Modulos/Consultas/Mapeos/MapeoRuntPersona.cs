using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;

/// <summary>
/// Parte de respuesta del RUNT persona (ADR-0075; antes en <c>FuenteRuntPersonaReal</c>, HU #13792, F05-C4): de
/// <c>ConsultarConductor</c> lee SOLO el chequeo <c>conductor_identidad</c> (inscrito si está OK), <c>person_license_status</c> y
/// <c>person_license_categories</c> (las categorías de las licencias activas). Nunca los campos de nombres ni la respuesta cruda.
/// Un proveedor caído (chequeo en ERROR), la respuesta sin resultado y los errores gRPC llegan traducidos por
/// <see cref="TraductorErroresConsultas"/>.
/// </summary>
/// <example>
/// <code>
/// var r = MapeoRuntPersona.Mapear(respuesta); // RespuestaFuente&lt;ResultadoPersonaRunt&gt;
/// </code>
/// </example>
public static class MapeoRuntPersona
{
    private const string ChequeoIdentidad = "conductor_identidad";
    private const string CampoEstado = "person_license_status";
    private const string CampoCategorias = "person_license_categories";
    private const string LicenciaActiva = "ACTIVA";

    public static RespuestaFuente<ResultadoPersonaRunt> Mapear(ConsultarConductorResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var resultado = respuesta.Resultado;
        var servicio = TraductorErroresConsultas.Servicio(resultado?.Proveedor);
        if (TraductorErroresConsultas.Traducir(resultado) is { } error)
            return RespuestaFuente<ResultadoPersonaRunt>.Fallo(error, servicio);

        return RespuestaFuente<ResultadoPersonaRunt>.Exito(Normalizar(resultado!), servicio, null);
    }

    public static RespuestaFuente<ResultadoPersonaRunt> Error(StatusCode codigo) =>
        RespuestaFuente<ResultadoPersonaRunt>.Fallo(TraductorErroresConsultas.Traducir(codigo));

    internal static ResultadoPersonaRunt Normalizar(ResultadoConsulta resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
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
