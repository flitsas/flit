using System.Text.Json.Serialization;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>
/// Saneamiento de un vehículo para la compañía (plan §6.3, §3.5; HU #14576 AC26). Tipo separado del de FLIT (SP-15): nunca trae
/// <c>usuario_id</c>, quién tomó, cerró o anuló, la referencia del almacén, el sha256 ni quién subió la evidencia. <c>version</c> es el
/// <c>xmin</c> del saneamiento. El historial no trae <c>RENGLON_ACTUALIZADO</c> ni <c>OBSERVACION_EDITADA</c> y su actor es
/// <c>COMPANIA</c> o <c>FLIT</c>.
/// </summary>
public sealed record SaneamientoClienteDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("version")] uint Version,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("solicitado_en")] DateTimeOffset SolicitadoEn,
    [property: JsonPropertyName("solicitado_por_mi")] bool SolicitadoPorMi,
    [property: JsonPropertyName("tomado_en")] DateTimeOffset? TomadoEn,
    [property: JsonPropertyName("cerrado_en")] DateTimeOffset? CerradoEn,
    [property: JsonPropertyName("motivo_cierre")] string? MotivoCierre,
    [property: JsonPropertyName("motivo_anulacion")] string? MotivoAnulacion,
    [property: JsonPropertyName("observacion_flit")] string? ObservacionFlit,
    [property: JsonPropertyName("renglones")] IReadOnlyList<RenglonClienteDto> Renglones,
    [property: JsonPropertyName("historial")] IReadOnlyList<EventoClienteDto> Historial);

/// <summary>Un renglón no quitado del saneamiento, para la compañía. <c>version</c> es su <c>xmin</c>.</summary>
public sealed record RenglonClienteDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("version")] uint Version,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("hallazgo")] string? Hallazgo,
    [property: JsonPropertyName("descripcion")] string? Descripcion,
    [property: JsonPropertyName("automatico")] bool Automatico,
    [property: JsonPropertyName("resultado")] string Resultado,
    [property: JsonPropertyName("nota")] string? Nota,
    [property: JsonPropertyName("evidencia")] EvidenciaClienteDto? Evidencia);

/// <summary>La evidencia de un renglón: solo nombre, tamaño e instante de carga (§6.3).</summary>
public sealed record EvidenciaClienteDto(
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("tamano")] long Tamano,
    [property: JsonPropertyName("subida_en")] DateTimeOffset SubidaEn);

/// <summary>Un evento del historial para la compañía: actor genérico, sin usuario.</summary>
public sealed record EventoClienteDto(
    [property: JsonPropertyName("evento")] string Evento,
    [property: JsonPropertyName("ocurrido_en")] DateTimeOffset OcurridoEn,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("fuente")] string? Fuente,
    [property: JsonPropertyName("motivo")] string? Motivo);

/// <summary>Respuesta del masivo <c>POST /saneamientos/solicitudes</c> (§6.1): cuántos se solicitaron y los omitidos con su motivo, en el orden pedido.</summary>
public sealed record SolicitudesSaneamientoDto(
    [property: JsonPropertyName("solicitados")] int Solicitados,
    [property: JsonPropertyName("omitidos")] IReadOnlyList<OmitidoSaneamientoDto> Omitidos);

/// <summary>Vehículo omitido del masivo: <c>NO_TERMINADO</c>, <c>YA_SOLICITADO</c> o <c>NO_ENCONTRADO</c> (este último también si el usuario no lo ve).</summary>
public sealed record OmitidoSaneamientoDto(
    [property: JsonPropertyName("vehiculo_id")] Guid VehiculoId,
    [property: JsonPropertyName("motivo")] string Motivo);

/// <summary>
/// El saneamiento en cada fila de la grilla del lote (§6.1, AC35): estado del no anulado (null si no hay), si se puede marcar y por
/// qué no (<c>NO_TERMINADO</c> o <c>YA_SOLICITADO</c>).
/// </summary>
public sealed record SaneamientoFilaDto(
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("elegible")] bool Elegible,
    [property: JsonPropertyName("motivo_no_elegible")] string? MotivoNoElegible);
