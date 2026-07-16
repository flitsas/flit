namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Máquina de estados del ciclo de vida del trámite (N 03, RF02). Pura. Opera sobre los
/// estados de negocio de <see cref="TramiteEstado"/> (los persistidos en
/// <c>procedure_instances.status</c>). <c>aprobado</c> y <c>anulado</c> son terminales (RF04).
/// Reemplaza la máquina interna de 14 estados de TRAM-12a (ADR-0022).
/// </summary>
public static class TramiteStateMachine
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Transitions =
        new(StringComparer.Ordinal)
        {
            [TramiteEstado.Borrador] = [TramiteEstado.Anulado, TramiteEstado.Preparado],
            // Feature #10587 / HU #10785 — la ruta de placa NO bifurca la máquina de estados: el
            // trámite entra a la decisión del OT siempre desde 'entregado'. El progreso de placa
            // (preasignado/asignado) es un sub-estado interno ortogonal (ver PlateFlowStateMachine).
            [TramiteEstado.Preparado] = [TramiteEstado.Entregado],
            [TramiteEstado.Entregado] = [TramiteEstado.Aprobado, TramiteEstado.Rechazado],
            [TramiteEstado.Rechazado] = [TramiteEstado.Borrador, TramiteEstado.Anulado],
            [TramiteEstado.Aprobado] = [],
            [TramiteEstado.Anulado] = [],
        };

    /// <summary>¿La transición <paramref name="from"/> → <paramref name="to"/> está permitida (RF02)?</summary>
    public static bool IsValidTransition(string? from, string? to) =>
        from is not null && to is not null
        && Transitions.TryGetValue(from, out var tos) && tos.Contains(to, StringComparer.Ordinal);

    /// <summary>Estados a los que se puede mover desde <paramref name="from"/> (para UI/acciones).</summary>
    public static IReadOnlyList<string> TransitionsFrom(string? from) =>
        from is not null && Transitions.TryGetValue(from, out var tos) ? tos : [];
}
