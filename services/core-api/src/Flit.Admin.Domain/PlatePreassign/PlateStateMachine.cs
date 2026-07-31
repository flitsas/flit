namespace Flit.Admin.Domain.PlatePreassign;

/// <summary>
/// Máquina de estados del ciclo de vida de una placa (Feature #10587). Pura. Enforced en el
/// repositorio antes de aplicar cualquier cambio de estado.
/// <code>
/// disponible ──[radica / OT asigna]──> preasignada ──[OT aprueba RUNT]──> utilizada (terminal)
/// disponible ──[OT bloquea]──> bloqueada ──[OT desbloquea]──> disponible
/// preasignada ──[OT revoca/rechaza]──> revocada ──[liberar]──> disponible
/// </code>
/// </summary>
public static class PlateStateMachine
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Transitions =
        new(StringComparer.Ordinal)
        {
            [PlateState.Disponible] = [PlateState.Preasignada, PlateState.Bloqueada],
            [PlateState.Preasignada] = [PlateState.Utilizada, PlateState.Revocada],
            [PlateState.Bloqueada] = [PlateState.Disponible],
            [PlateState.Revocada] = [PlateState.Disponible],
            [PlateState.Utilizada] = [],
        };

    /// <summary>¿La transición <paramref name="from"/> → <paramref name="to"/> está permitida?</summary>
    public static bool IsValidTransition(string? from, string? to) =>
        from is not null && to is not null
        && Transitions.TryGetValue(from, out var tos) && tos.Contains(to, StringComparer.Ordinal);

    /// <summary>Estados a los que se puede mover desde <paramref name="from"/>.</summary>
    public static IReadOnlyList<string> TransitionsFrom(string? from) =>
        from is not null && Transitions.TryGetValue(from, out var tos) ? tos : [];
}
