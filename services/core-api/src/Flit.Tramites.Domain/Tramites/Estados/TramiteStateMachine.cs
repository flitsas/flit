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
            // Preparado bifurca: ruta estándar (entregado) o ruta de preasignación de placa
            // (Feature #10587): asignado si la compañía eligió placa de un rango; preasignado si
            // no hay rango y el trámite se envía al OT para que la asigne (Flujo B).
            [TramiteEstado.Preparado] = [TramiteEstado.Entregado, TramiteEstado.Asignado, TramiteEstado.Preasignado],
            [TramiteEstado.Entregado] = [TramiteEstado.Aprobado, TramiteEstado.Rechazado],
            // Preasignado: el OT asigna la placa (→ asignado) o se anula.
            [TramiteEstado.Preasignado] = [TramiteEstado.Asignado, TramiteEstado.Anulado],
            // Asignado: tras SOAT + recepción del OT, se aprueba o rechaza (o se anula). Revocar la
            // preasignación devuelve el trámite a preasignado para reasignar placa (Feature #10587).
            [TramiteEstado.Asignado] = [TramiteEstado.Aprobado, TramiteEstado.Rechazado, TramiteEstado.Anulado, TramiteEstado.Preasignado],
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
