using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;

/// <summary>
/// Parte de respuesta del RUES (ADR-0075; antes en <c>FuenteRuesReal</c>, HU #13792, F05-C9): lee SOLO el estado y la fecha de
/// renovación, de la primera matrícula mercantil certificada (<c>estado</c>, <c>renovada_en</c>) o, si no viene, de los campos
/// <c>rues_estado</c>, <c>rues_fecha_renovacion</c> o <c>rues_ultimo_ano_renovado</c>. Nunca la razón social, el NIT, la
/// dirección ni los representantes. Los errores llegan traducidos por <see cref="TraductorErroresConsultas"/>.
/// </summary>
/// <example>
/// <code>
/// var r = MapeoRues.Mapear(respuesta); // RespuestaFuente&lt;ResultadoRues&gt;
/// </code>
/// </example>
public static class MapeoRues
{
    public static RespuestaFuente<ResultadoRues> Mapear(ConsultarRuesResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var resultado = respuesta.Resultado;
        var servicio = TraductorErroresConsultas.Servicio(resultado?.Proveedor);
        if (TraductorErroresConsultas.Traducir(resultado) is { } error)
            return RespuestaFuente<ResultadoRues>.Fallo(error, servicio);

        return RespuestaFuente<ResultadoRues>.Exito(Normalizar(resultado!), servicio, null);
    }

    public static RespuestaFuente<ResultadoRues> Error(StatusCode codigo) =>
        RespuestaFuente<ResultadoRues>.Fallo(TraductorErroresConsultas.Traducir(codigo));

    internal static ResultadoRues Normalizar(ResultadoConsulta resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var matricula = resultado.Certificaciones?.MatriculasMercantiles.FirstOrDefault();
        var estado = Texto(matricula?.Estado?.Crudo) ?? Vigencia(matricula?.Estado) ?? Campo(resultado, "rues_estado");
        var renovacion = Texto(matricula?.RenovadaEn?.Valor) ?? Campo(resultado, "rues_fecha_renovacion") ?? Campo(resultado, "rues_ultimo_ano_renovado");
        return new ResultadoRues(estado is not null || renovacion is not null, estado?.ToUpperInvariant(), renovacion);
    }

    private static string? Vigencia(EstadoVigencia? estado) => estado?.Valor switch
    {
        Flit.Consultas.Grpc.V1.Vigencia.Vigente => "ACTIVA",
        Flit.Consultas.Grpc.V1.Vigencia.Vencido => "VENCIDA",
        _ => null,
    };

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string? Campo(ResultadoConsulta resultado, string clave) =>
        resultado.Campos.FirstOrDefault(c => c.Clave == clave && c.HasValorTexto) is { } campo ? Texto(campo.ValorTexto) : null;
}
