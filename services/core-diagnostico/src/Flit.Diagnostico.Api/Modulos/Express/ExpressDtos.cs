using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Express;

/// <summary>
/// Cuerpo de <c>POST /express</c> y de <c>POST /express/estimado</c> (HU #13753, F02-H6): la cabecera (nombre opcional, modo,
/// tipo de trámite y alcance, como en <c>POST /diagnosticos</c>) y el único vehículo (identificador y, opcional, el documento
/// del locatario). Es una clase (no un record) para que ningún <c>ToString</c> automático lleve el documento a un log.
/// </summary>
public sealed class ExpressEntradaDto
{
    /// <summary>Nombre del diagnóstico; vacío o ausente se autogenera con el identificador y la fecha.</summary>
    [JsonPropertyName("nombre")]
    public string? Nombre { get; init; }

    [JsonPropertyName("modo")]
    public string? Modo { get; init; }

    [JsonPropertyName("tipo_tramite_codigo")]
    public string? TipoTramiteCodigo { get; init; }

    [JsonPropertyName("alcance")]
    public AlcanceDto? Alcance { get; init; }

    /// <summary>Placa o VIN del vehículo.</summary>
    [JsonPropertyName("identificador")]
    public string? Identificador { get; init; }

    [JsonPropertyName("tipo_documento_locatario")]
    public string? TipoDocumentoLocatario { get; init; }

    [JsonPropertyName("documento_locatario")]
    public string? DocumentoLocatario { get; init; }
}

/// <summary>
/// Respuesta 201 del Express: la cabecera creada (<c>es_express=true</c>, en CREADO), su único vehículo (documento del
/// locatario enmascarado) y, solo con <c>estimado_consultas=ANTES_DE_LANZAR</c> en la copia de configuración, el estimado.
/// </summary>
public sealed record ExpressCreadoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("modo")] string Modo,
    [property: JsonPropertyName("es_express")] bool EsExpress,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("creado_en")] DateTimeOffset CreadoEn,
    [property: JsonPropertyName("vehiculo")] VehiculoConfirmadoDto Vehiculo,
    [property: JsonPropertyName("estimado_consultas"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<EstimadoFuenteDto>? EstimadoConsultas);

/// <summary>
/// Respuesta de <c>POST /express/estimado</c>: si la configuración de la compañía muestra el estimado antes de lanzar y, en
/// ese caso, el estimado por fuente del único vehículo. No se persiste nada.
/// </summary>
public sealed record EstimadoExpressDto(
    [property: JsonPropertyName("mostrar_estimado")] bool MostrarEstimado,
    [property: JsonPropertyName("estimado_consultas"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<EstimadoFuenteDto>? EstimadoConsultas);
