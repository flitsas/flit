using System.Text.Json;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Observación de subsanación (HU #10871): checklist HÍBRIDO que acompaña una transición a
/// <c>subsanacion</c> (N 03). Se persiste en <c>procedure_instance_status_history.metadata</c>
/// (columna jsonb que existía sin usar) junto al <c>reason</c> de texto libre ya existente en la
/// columna <c>reason</c> (RF05): <c>reason</c> sigue siendo el motivo general legible del
/// historial; <c>metadata</c> agrega <see cref="Motivo"/> (redundante, para que el JSON sea
/// autocontenible) y el detalle ESTRUCTURADO por ítem subsanable.
/// </summary>
/// <remarks>
/// Dos disparadores producen esta observación:
/// <list type="bullet">
///   <item>AC1 — el Organismo de Tránsito observa/rechaza un trámite <c>entregado</c> con un
///   checklist de ítems (ver <c>RejectOtClientProcedureHandler</c> / <c>OtClientProcedureRepository.ObserveAsync</c>).</item>
///   <item>AC2 — un callback de rechazo de una integración (Quipux u otra registrada vía webhook,
///   p. ej. RNMC) trae solo el motivo, sin checklist estructurado: <see cref="Items"/> queda vacío
///   (ver <c>ConsultarEstadoQuipuxHandler</c> / <c>ProcessOtWebhookCallbackHandler</c>).</item>
/// </list>
/// </remarks>
public sealed record SubsanacionObservation
{
    /// <summary>Motivo general de la observación/rechazo.</summary>
    public string? Motivo { get; init; }

    /// <summary>Checklist de ítems subsanables (campo/documento + detalle de la corrección).</summary>
    public IReadOnlyList<SubsanacionObservationItem> Items { get; init; } = [];

    /// <summary>
    /// HU #10872 (AC1) — snapshot de <c>field_values</c> (clave→valor canónico) capturado en el MISMO
    /// instante en que el trámite entra a <c>subsanacion</c>. Es el baseline del DIFF que la
    /// re-radicación (<c>subsanacion → entregado</c>) computa para re-evaluar SOLO los gates de lo
    /// corregido (ver <see cref="Tramites.Services.FieldValueSnapshot"/> /
    /// <see cref="Tramites.Services.SubsanacionGateMap"/>). <c>null</c> = observación previa a esta HU
    /// (sin snapshot): el re-radicado degrada a re-evaluar TODOS los gates (fail-safe).
    /// </summary>
    public IReadOnlyDictionary<string, string?>? FieldSnapshot { get; init; }

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Serializa la observación al JSON HÍBRIDO que se persiste en <c>metadata</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializeOptions);

    /// <summary>Deserialización tolerante: JSON nulo/vacío/corrupto degrada a <c>null</c> (metadata es
    /// configuración/observabilidad, no un invariante del dominio).</summary>
    public static SubsanacionObservation? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SubsanacionObservation>(json, DeserializeOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Ítem subsanable del checklist: qué campo/documento corregir y el detalle de la corrección.</summary>
public sealed record SubsanacionObservationItem
{
    /// <summary>Campo o clave subsanable (p. ej. el <c>field_key</c> del wizard, o un código de documento).</summary>
    public string? Campo { get; init; }

    /// <summary>Detalle de qué corregir en ese campo/ítem.</summary>
    public string? Detalle { get; init; }
}
