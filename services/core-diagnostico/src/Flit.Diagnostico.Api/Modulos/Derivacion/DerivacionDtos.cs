using System.Text.Json.Serialization;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// <c>derivacion</c> de una fila de la grilla (Punto 4, diseño §5.1): si se puede crear el trámite y, si no, por qué
/// (<c>NO_TERMINADO</c>, <c>NO_VIABLE</c>, <c>YA_TIENE_TRAMITE</c>, <c>EN_CURSO</c>), el estado del último ítem del vehículo
/// (<c>PENDIENTE</c>, <c>ENVIADO</c>, <c>CREADO</c>, <c>OMITIDO</c>, <c>FALLIDO</c>; null si nunca se intentó) y el trámite creado.
/// </summary>
public sealed record DerivacionFilaDto(
    [property: JsonPropertyName("elegible")] bool Elegible,
    [property: JsonPropertyName("motivo_no_elegible")] string? MotivoNoElegible,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("tramite")] TramiteFilaDto? Tramite);

/// <summary>El borrador creado en Trámites: id, número (<c>reference_number</c>) y el enlace «Ver trámite #N».</summary>
public sealed record TramiteFilaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("numero")] string Numero,
    [property: JsonPropertyName("url")] string Url);
