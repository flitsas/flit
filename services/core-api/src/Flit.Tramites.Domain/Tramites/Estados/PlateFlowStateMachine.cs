namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Máquina del sub-estado INTERNO de placa. Pura. Opera sobre <see cref="PlateFlowStatus"/> de forma
/// ortogonal a <see cref="TramiteStateMachine"/>. <c>null</c> = sin ruta de placa.
/// <code>
/// (null) --[sin placa]--> preasignado --[OT asigna]--> asignado --[gestor procesa]--> terminado
/// (null) --[placa completa]--> asignado --[gestor procesa]--> terminado
/// (null) --[placa + skip compañía]--> terminado
/// asignado --[OT revoca]--> preasignado
/// terminado|asignado|preasignado --[OT aprueba/rechaza]--> null
/// </code>
/// </summary>
public static class PlateFlowStateMachine
{
    private const string Null = "";

    private static string Key(string? s) => s ?? Null;

    private static readonly Dictionary<string, IReadOnlyList<string>> Transitions =
        new(StringComparer.Ordinal)
        {
            [Null] =
            [
                PlateFlowStatus.Preasignado,
                PlateFlowStatus.Asignado,
                PlateFlowStatus.Terminado,
            ],
            [PlateFlowStatus.Preasignado] = [PlateFlowStatus.Asignado, Null],
            [PlateFlowStatus.Asignado] =
            [
                PlateFlowStatus.Terminado,
                PlateFlowStatus.Preasignado,
                Null,
            ],
            [PlateFlowStatus.Terminado] = [Null],
        };

    /// <summary>¿La transición de sub-estado <paramref name="from"/> → <paramref name="to"/> está permitida?</summary>
    public static bool IsValidTransition(string? from, string? to) =>
        Transitions.TryGetValue(Key(from), out var tos)
        && (tos.Contains(Key(to), StringComparer.Ordinal) || Key(from) == Key(to));
}
