using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Respuesta de <c>GET /diagnosticos/{id}/grilla?desde=</c> (HU #13743, F04-H1, plan §6.1 fila 04). <c>version</c> es la
/// versión actual del tablero del lote: el cliente la manda como <c>desde</c> en el siguiente sondeo y recibe solo las
/// celdas con <c>version</c> mayor (con <c>desde=0</c>, todo). <c>vehiculos</c> trae solo los vehículos con alguna celda
/// cambiada y, de cada uno, solo esas celdas; <c>terminado</c> se calcula con TODAS sus celdas (R-7). El estado global,
/// las columnas y el avance van siempre. DTO de cliente: nunca lleva el origen de la celda, el usuario que la llenó a mano,
/// el payload ni documentos (R-20; <c>GrillaTests</c> lo verifica por reflexión). <c>estimado_consultas</c> solo aparece con
/// <c>estimado_consultas=AL_FINALIZAR</c> en la copia de configuración del lote y el lote cerrado (R-11b).
/// </summary>
public sealed record GrillaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("desde")] long Desde,
    [property: JsonPropertyName("columnas")] AlcanceDto Columnas,
    [property: JsonPropertyName("avance")] AvanceLoteDto Avance,
    [property: JsonPropertyName("vehiculos")] IReadOnlyList<FilaVehiculoDto> Vehiculos,
    [property: JsonPropertyName("estimado_consultas"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<EstimadoVsConsumidoDto>? EstimadoConsultas);

/// <summary>Avance del lote: vehículos en total y cuántos ya terminaron (todas sus celdas en COMPLETADO o NA, R-7).</summary>
public sealed record AvanceLoteDto(
    [property: JsonPropertyName("vehiculos")] int Vehiculos,
    [property: JsonPropertyName("terminados")] int Terminados);

/// <summary>Una fila de la grilla: el vehículo y sus celdas (solo las cambiadas desde <c>desde</c>).</summary>
public sealed record FilaVehiculoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("orden")] int Orden,
    [property: JsonPropertyName("identificador")] string Identificador,
    [property: JsonPropertyName("tipo_identificador")] string TipoIdentificador,
    [property: JsonPropertyName("placa")] string? Placa,
    [property: JsonPropertyName("vin")] string? Vin,
    [property: JsonPropertyName("terminado")] bool Terminado,
    [property: JsonPropertyName("celdas")] IReadOnlyList<CeldaGrillaDto> Celdas);

/// <summary>
/// Una celda del semáforo: fuente, estado, motivo de N/A, tipo de error y la fecha y hora del dato (D-10; en la reutilizada,
/// la del dato original). Sin origen, usuario manual, payload ni documentos (R-20).
/// </summary>
public sealed record CeldaGrillaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("motivo_na")] string? MotivoNa,
    [property: JsonPropertyName("tipo_error")] string? TipoError,
    [property: JsonPropertyName("dato_obtenido_en")] DateTimeOffset? DatoObtenidoEn,
    [property: JsonPropertyName("reutilizada")] bool Reutilizada,
    [property: JsonPropertyName("version")] long Version);

/// <summary>
/// Estimado por fuente (calculador de HU #13747) frente a las consultas consumidas: intentos con <c>desde_cache=false</c>
/// (las reutilizadas del mismo día no cuentan, D-10) y sin los de llenado manual.
/// </summary>
public sealed record EstimadoVsConsumidoDto(
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("estimadas")] int Estimadas,
    [property: JsonPropertyName("consumidas")] int Consumidas);
