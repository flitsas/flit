using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Estimacion;

namespace Flit.Diagnostico.Api.Modulos.Carga;

/// <summary>
/// Resumen de validación de <c>POST /diagnosticos/{id}/carga</c> (HU #13749, F02-H4): conteos, una fila por identificador
/// (los duplicados del archivo consolidados en la primera), avisos y, solo con <c>estimado_consultas=ANTES_DE_LANZAR</c>,
/// el estimado por fuente. Se cumple <c>total_filas = validos + erroneos + duplicados</c>. El documento del locatario va
/// siempre enmascarado. No se guarda nada: el lote se confirma en F02-H5.
/// </summary>
public sealed record ResumenValidacionDto(
    [property: JsonPropertyName("total_filas")] int TotalFilas,
    [property: JsonPropertyName("validos")] int Validos,
    [property: JsonPropertyName("erroneos")] int Erroneos,
    [property: JsonPropertyName("duplicados")] int Duplicados,
    [property: JsonPropertyName("puede_continuar")] bool PuedeContinuar,
    [property: JsonPropertyName("limite_vehiculos_lote")] int LimiteVehiculosLote,
    [property: JsonPropertyName("supera_limite")] bool SuperaLimite,
    [property: JsonPropertyName("avisos")] IReadOnlyList<AvisoCargaDto> Avisos,
    [property: JsonPropertyName("filas")] IReadOnlyList<FilaResumenDto> Filas,
    [property: JsonPropertyName("estimado_consultas"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<EstimadoFuenteDto>? EstimadoConsultas);

/// <summary>Aviso del resumen: <c>SIN_VALIDOS</c> o <c>LIMITE_SUPERADO</c>.</summary>
public sealed record AvisoCargaDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("mensaje")] string Mensaje);

/// <summary>
/// Una fila del archivo (o la primera de un identificador repetido, con <c>filas_duplicadas</c>). <c>fila</c> es el número
/// de fila de la hoja o del CSV, con el encabezado en la fila 1.
/// </summary>
public sealed record FilaResumenDto(
    [property: JsonPropertyName("fila")] int Fila,
    [property: JsonPropertyName("identificador")] string Identificador,
    [property: JsonPropertyName("identificador_norm")] string IdentificadorNorm,
    [property: JsonPropertyName("tipo_identificador")] string? TipoIdentificador,
    [property: JsonPropertyName("tipo_documento_locatario")] string? TipoDocumentoLocatario,
    [property: JsonPropertyName("documento_locatario")] string? DocumentoLocatario,
    [property: JsonPropertyName("valida")] bool Valida,
    [property: JsonPropertyName("filas_duplicadas")] IReadOnlyList<int> FilasDuplicadas,
    [property: JsonPropertyName("errores")] IReadOnlyList<ErrorFilaDto> Errores);

/// <summary>Error de una fila: la columna de la plantilla, un código estable y el mensaje para el usuario.</summary>
public sealed record ErrorFilaDto(
    [property: JsonPropertyName("campo")] string Campo,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("mensaje")] string Mensaje);

/// <summary>
/// Consultas previstas de una fuente del alcance (código de la base), HU #13747. <c>minimo=true</c> (HU #13796, Q7) marca las
/// fuentes de persona antes de lanzar: las personas aún no se conocen y el número es «al menos N» (uno por vehículo;
/// locatario, uno por vehículo con documento declarado). Las fuentes del vehículo siempre llevan <c>minimo=false</c>.
/// </summary>
public sealed record EstimadoFuenteDto(
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("consultas")] int Consultas,
    [property: JsonPropertyName("minimo")] bool Minimo)
{
    /// <summary>El DTO de un estimado del dominio.</summary>
    internal static EstimadoFuenteDto De(EstimadoPorFuente estimado) =>
        new(CodigosDeEnum.Codigo(estimado.Fuente), estimado.Consultas, estimado.Minimo);
}
