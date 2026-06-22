using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Workflow STT del traspaso (paridad <c>tramite-workflow.ts</c> de Johan). Puro.
/// Terminales: <see cref="TramiteEstadoStt.Entregado"/>, <see cref="TramiteEstadoStt.Anulado"/>.
/// </summary>
public static class SttWorkflow
{
    private static readonly Dictionary<TramiteEstadoStt, IReadOnlyList<TramiteEstadoStt>> Transitions =
        new()
        {
            [TramiteEstadoStt.Radicado] =
                [TramiteEstadoStt.EnValidacion, TramiteEstadoStt.Subsanacion, TramiteEstadoStt.Anulado],
            [TramiteEstadoStt.EnValidacion] =
                [TramiteEstadoStt.Subsanacion, TramiteEstadoStt.EnTramite, TramiteEstadoStt.Rechazado, TramiteEstadoStt.Anulado],
            [TramiteEstadoStt.Subsanacion] =
                [TramiteEstadoStt.EnValidacion, TramiteEstadoStt.Anulado],
            [TramiteEstadoStt.EnTramite] =
                [TramiteEstadoStt.Aprobado, TramiteEstadoStt.Subsanacion, TramiteEstadoStt.Rechazado, TramiteEstadoStt.Anulado],
            [TramiteEstadoStt.Aprobado] =
                [TramiteEstadoStt.Entregado, TramiteEstadoStt.Anulado],
            [TramiteEstadoStt.Rechazado] =
                [TramiteEstadoStt.Subsanacion, TramiteEstadoStt.Anulado],
            [TramiteEstadoStt.Entregado] = [],
            [TramiteEstadoStt.Anulado] = [],
        };

    /// <summary>¿La transición STT <paramref name="from"/> → <paramref name="to"/> está permitida?</summary>
    public static bool IsValidTransition(TramiteEstadoStt from, TramiteEstadoStt to) =>
        Transitions.TryGetValue(from, out var tos) && tos.Contains(to);

    /// <summary>Estados a los que se puede mover desde <paramref name="from"/> (para UI).</summary>
    public static IReadOnlyList<TramiteEstadoStt> TransitionsFrom(TramiteEstadoStt from) =>
        Transitions.TryGetValue(from, out var tos) ? tos : [];

    /// <summary>El gestor CEA puede editar el expediente (paridad <c>traspasoExpedienteEditable</c>).</summary>
    public static bool ExpedienteEditable(TramiteEstadoStt estado) =>
        estado is TramiteEstadoStt.Radicado or TramiteEstadoStt.Subsanacion;

    /// <summary>Gestión cerrada → wizard/anexos en solo lectura (salvo subsanación) (paridad <c>traspasoGestionCerrada</c>).</summary>
    public static bool GestionCerrada(TramiteEstadoStt estado) =>
        !ExpedienteEditable(estado);

    /// <summary>Número de radicado <c>TD-YYYY-NNNNN</c> (paridad <c>formatRadicado</c>).</summary>
    public static string FormatRadicado(int seq, int year) =>
        $"TD-{year}-{seq.ToString("D5", CultureInfo.InvariantCulture)}";
}
