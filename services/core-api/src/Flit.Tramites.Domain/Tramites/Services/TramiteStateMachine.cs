using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Máquina de estados INTERNA del trámite (paridad <c>VALID_TRANSITIONS</c> / <c>isValidTransition</c>
/// de Johan, TRAM-12a). Pura. <see cref="TramiteEstadoInterno.Completado"/> es terminal.
/// </summary>
public static class TramiteStateMachine
{
    private static readonly Dictionary<TramiteEstadoInterno, IReadOnlyList<TramiteEstadoInterno>> Transitions =
        new()
        {
            [TramiteEstadoInterno.Borrador] =
            [
                TramiteEstadoInterno.Radicado, TramiteEstadoInterno.EnValidacion, TramiteEstadoInterno.Documentos,
                TramiteEstadoInterno.Identidad, TramiteEstadoInterno.Aprobado, TramiteEstadoInterno.EnviadoTransito,
            ],
            [TramiteEstadoInterno.Radicado] =
                [TramiteEstadoInterno.EnValidacion, TramiteEstadoInterno.Documentos, TramiteEstadoInterno.Rechazado],
            [TramiteEstadoInterno.EnValidacion] =
                [TramiteEstadoInterno.Documentos, TramiteEstadoInterno.Rechazado],
            [TramiteEstadoInterno.Documentos] =
                [TramiteEstadoInterno.Identidad, TramiteEstadoInterno.Rechazado],
            [TramiteEstadoInterno.Identidad] =
                [TramiteEstadoInterno.Aprobado, TramiteEstadoInterno.Rechazado],
            [TramiteEstadoInterno.Aprobado] =
                [TramiteEstadoInterno.EnviadoTransito, TramiteEstadoInterno.Rechazado],
            [TramiteEstadoInterno.Rechazado] =
                [TramiteEstadoInterno.Borrador],
            [TramiteEstadoInterno.EnviadoTransito] =
                [TramiteEstadoInterno.Rechazado],
            [TramiteEstadoInterno.RecibidoTransito] =
                [TramiteEstadoInterno.PlacaPreasignada, TramiteEstadoInterno.EnviadoTransito, TramiteEstadoInterno.Rechazado],
            [TramiteEstadoInterno.PlacaPreasignada] =
                [TramiteEstadoInterno.SolicitudSoat, TramiteEstadoInterno.RecibidoTransito, TramiteEstadoInterno.Rechazado],
            [TramiteEstadoInterno.SolicitudSoat] =
                [TramiteEstadoInterno.SoatComprado],
            [TramiteEstadoInterno.SoatComprado] =
                [TramiteEstadoInterno.SoatVerificado],
            [TramiteEstadoInterno.SoatVerificado] =
                [TramiteEstadoInterno.Completado],
            [TramiteEstadoInterno.Completado] =
                [],
        };

    /// <summary>¿La transición <paramref name="from"/> → <paramref name="to"/> está permitida?</summary>
    public static bool IsValidTransition(TramiteEstadoInterno from, TramiteEstadoInterno to) =>
        Transitions.TryGetValue(from, out var tos) && tos.Contains(to);

    /// <summary>Estados a los que se puede mover desde <paramref name="from"/> (para UI).</summary>
    public static IReadOnlyList<TramiteEstadoInterno> TransitionsFrom(TramiteEstadoInterno from) =>
        Transitions.TryGetValue(from, out var tos) ? tos : [];
}
