using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Vehiculos;

/// <summary>
/// Una fila capturada de validar-filas (HU #13751): el identificador y, opcional, el documento del locatario. La
/// confirmación ya no recibe filas en JSON: reenvía el archivo (<see cref="LoteDesdeArchivo"/>). <c>fila</c> es el número de fila del archivo para devolverlo en el resumen. Es una clase (no un record)
/// para que ningún <c>ToString</c> automático lleve el documento a un log.
/// </summary>
public sealed class FilaCapturadaDto
{
    [JsonPropertyName("fila")]
    public int? Fila { get; init; }

    [JsonPropertyName("identificador")]
    public string? Identificador { get; init; }

    [JsonPropertyName("tipo_documento_locatario")]
    public string? TipoDocumentoLocatario { get; init; }

    [JsonPropertyName("documento_locatario")]
    public string? DocumentoLocatario { get; init; }
}

/// <summary>Cuerpo de <c>POST /diagnosticos/{id}/carga/validar-filas</c>.</summary>
public sealed class FilasAValidarDto
{
    [JsonPropertyName("filas")]
    public IReadOnlyList<FilaCapturadaDto>? Filas { get; init; }
}

/// <summary>
/// Respuesta de la confirmación: los vehículos guardados (documento enmascarado), lo descartado y, solo con
/// <c>estimado_consultas=ANTES_DE_LANZAR</c> en la copia de configuración, el estimado por fuente.
/// </summary>
public sealed record ResultadoConfirmacionDto(
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("confirmados")] int Confirmados,
    [property: JsonPropertyName("invalidos")] int Invalidos,
    [property: JsonPropertyName("duplicados")] int Duplicados,
    [property: JsonPropertyName("limite_vehiculos_lote")] int LimiteVehiculosLote,
    [property: JsonPropertyName("vehiculos")] IReadOnlyList<VehiculoConfirmadoDto> Vehiculos,
    [property: JsonPropertyName("estimado_consultas"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<EstimadoFuenteDto>? EstimadoConsultas);

/// <summary>Un vehículo guardado del lote; el documento del locatario va enmascarado («****» y los 4 últimos).</summary>
public sealed record VehiculoConfirmadoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("orden")] int Orden,
    [property: JsonPropertyName("identificador")] string Identificador,
    [property: JsonPropertyName("identificador_norm")] string IdentificadorNorm,
    [property: JsonPropertyName("tipo_identificador")] string TipoIdentificador,
    [property: JsonPropertyName("tipo_documento_locatario")] string? TipoDocumentoLocatario,
    [property: JsonPropertyName("documento_locatario")] string? DocumentoLocatario);
