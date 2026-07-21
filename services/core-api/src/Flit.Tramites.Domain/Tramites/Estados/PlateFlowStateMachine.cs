namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Máquina del sub-estado INTERNO de placa (Feature #10587, HU #10785). Pura. Opera sobre
/// <see cref="PlateFlowStatus"/> (persistido en <c>procedure_instances.plate_flow_status</c>) de forma
/// ortogonal a <see cref="TramiteStateMachine"/>: el status global permanece en <c>entregado</c> mientras
/// el sub-flujo avanza. <c>null</c> representa "sin ruta de placa".
/// <code>
/// (null) --[radicación Flujo B: sin placa]--> preasignado --[OT registra placa]--> asignado
/// (null) --[radicación Flujo A: placa elegida]----------------------------------> asignado
/// asignado --[OT revoca preasignación]--> preasignado
/// </code>
/// Al aprobar/rechazar el OT, la decisión limpia el sub-estado (deja de gobernar; la placa pasa a
/// utilizada/liberada), por lo que <c>*→null</c> es válido como cierre del sub-flujo.
/// </summary>
public static class PlateFlowStateMachine
{
    private const string Null = "";

    private static string Key(string? s) => s ?? Null;

    private static readonly Dictionary<string, IReadOnlyList<string>> Transitions =
        new(StringComparer.Ordinal)
        {
            // Sin ruta de placa: radicar puede fijar el sub-estado inicial (Flujo A o B).
            [Null] = [PlateFlowStatus.Preasignado, PlateFlowStatus.Asignado],
            // Preasignado: el OT registra la placa (→ asignado) o el sub-flujo se cierra (→ null).
            [PlateFlowStatus.Preasignado] = [PlateFlowStatus.Asignado, Null],
            // Asignado: el OT revoca (→ preasignado) o el sub-flujo se cierra (→ null).
            [PlateFlowStatus.Asignado] = [PlateFlowStatus.Preasignado, Null],
        };

    /// <summary>¿La transición de sub-estado <paramref name="from"/> → <paramref name="to"/> está permitida?</summary>
    public static bool IsValidTransition(string? from, string? to) =>
        Transitions.TryGetValue(Key(from), out var tos)
        && (tos.Contains(Key(to), StringComparer.Ordinal) || Key(from) == Key(to));
}
