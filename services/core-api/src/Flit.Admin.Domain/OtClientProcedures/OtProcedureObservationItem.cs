namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>
/// Ítem subsanable del checklist de observación del Organismo de Tránsito (HU #10871, AC1): qué
/// campo/documento debe corregirse y el detalle de la corrección. Junto al motivo general, se
/// persiste en <c>procedure_instance_status_history.metadata</c> (checklist HÍBRIDO) cuando la
/// decisión del OT transiciona el trámite a <c>subsanacion</c> en vez de <c>rechazado</c>
/// (ver <see cref="IOtClientProcedureRepository.ObserveAsync"/>).
/// </summary>
public sealed record OtProcedureObservationItem
{
    /// <summary>Campo o clave subsanable (p. ej. el <c>field_key</c> del wizard, o un código de documento).</summary>
    public string? Campo { get; init; }

    /// <summary>Detalle de qué corregir en ese campo/ítem.</summary>
    public string? Detalle { get; init; }
}
