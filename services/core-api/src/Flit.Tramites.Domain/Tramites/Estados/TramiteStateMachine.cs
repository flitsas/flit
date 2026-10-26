namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Máquina de estados del ciclo de vida del trámite (N 03, RF02). Pura. Opera sobre los
/// estados de negocio de <see cref="TramiteEstado"/> (los persistidos en
/// <c>procedure_instances.status</c>). <c>aprobado</c>, <c>anulado</c> y <c>revocado</c> son
/// terminales (RF04).
/// <para>
/// Es la capa ESTRUCTURAL: dice qué aristas existen. Las aristas de la ruta de placa (ADR-0059) y la
/// re-radicación exigen además contexto (¿el tipo pide placa? ¿hay placa? ¿quién actúa? ¿subsanación
/// activa?), que resuelve <see cref="TramiteTransitionPolicy"/> por encima de esta máquina. Quien
/// necesite validar una transición real debe usar la política, no este diccionario a secas.
/// </para>
/// <para>
/// La subsanación NO es un estado: se activa con flag sobre <c>rechazado</c>. Re-radicar
/// (<c>rechazado → entregado | preasignacion</c>) solo es válida cuando <c>subsanacion_activa</c>.
/// </para>
/// </summary>
public static class TramiteStateMachine
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Transitions =
        new(StringComparer.Ordinal)
        {
            [TramiteEstado.Borrador] = [TramiteEstado.Anulado, TramiteEstado.Preparado],
            // ADR-0059 — al radicar (canal gestor/OT), la Ruta Larga entra por 'preasignacion'; el resto
            // por 'entregado'. El canal Quipux NO usa esta arista desde el submit: se queda en
            // 'preparado' hasta que el worker radica (actor Quipux → entregado).
            [TramiteEstado.Preparado] = [TramiteEstado.Entregado, TramiteEstado.Preasignacion],
            // Cola de placa del OT: asigna (→ asignado) o rechaza (→ rechazado, con marca de origen).
            // NO puede aprobar: la decisión sigue siendo exclusiva de 'entregado'.
            [TramiteEstado.Preasignacion] = [TramiteEstado.Asignado, TramiteEstado.Rechazado],
            // Con placa: el gestor «Envía al OT» (→ entregado) o el OT «Libera la placa» (→ preasignacion).
            // Sin decisión del OT aquí: ni aprobar ni rechazar.
            [TramiteEstado.Asignado] = [TramiteEstado.Entregado, TramiteEstado.Preasignacion],
            [TramiteEstado.Entregado] = [TramiteEstado.Aprobado, TramiteEstado.Rechazado],
            // Rechazado → entregado | preasignacion: re-radicación gestor/OT tras subsanación.
            // Rechazado → preparado: re-radicación canal Quipux (Bug #13912): el worker vuelve a
            // encolar; 'entregado' solo tras código 081/082. Política: exige subsanacion_activa.
            [TramiteEstado.Rechazado] =
            [
                TramiteEstado.Borrador,
                TramiteEstado.Anulado,
                TramiteEstado.Entregado,
                TramiteEstado.Preasignacion,
                TramiteEstado.Preparado,
            ],
            // HU #12166 (Feature #12156) — única salida de 'aprobado': el OT revoca su propia
            // aprobación. El admin de FLIT NO tiene esta transición disponible (su "Cambiar estado" y
            // "Anular" excluyen 'aprobado' explícitamente); solo el endpoint OT de revocación la usa.
            [TramiteEstado.Aprobado] = [TramiteEstado.Revocado],
            [TramiteEstado.Anulado] = [],
            [TramiteEstado.Revocado] = [],
        };

    /// <summary>¿La arista <paramref name="from"/> → <paramref name="to"/> existe en la máquina (RF02)?</summary>
    public static bool IsValidTransition(string? from, string? to) =>
        from is not null && to is not null
        && Transitions.TryGetValue(from, out var tos) && tos.Contains(to, StringComparer.Ordinal);

    /// <summary>Estados a los que se puede mover desde <paramref name="from"/> (para UI/acciones).</summary>
    public static IReadOnlyList<string> TransitionsFrom(string? from) =>
        from is not null && Transitions.TryGetValue(from, out var tos) ? tos : [];
}
