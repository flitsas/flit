namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>Resultado de evaluar una transición con contexto. <c>ErrorCode</c> null = permitida.</summary>
public sealed record TramiteTransitionVerdict(string? ErrorCode, string? Detail)
{
    public bool Allowed => ErrorCode is null;

    public static readonly TramiteTransitionVerdict Permitida = new(null, null);

    public static TramiteTransitionVerdict Denegada(string errorCode, string detail) => new(errorCode, detail);
}

/// <summary>
/// Política de transición del ciclo de vida (ADR-0059). Pura. Se apoya en la máquina estructural
/// (<see cref="TramiteStateMachine"/>) y añade las reglas que dependen del contexto:
/// <list type="bullet">
/// <item>La ruta de placa (<c>preasignacion</c>/<c>asignado</c>) solo existe para tipos que piden placa.</item>
/// <item>Radicar sin placa un tipo que la pide entra por <c>preasignacion</c>, nunca por <c>entregado</c>
/// — salvo Quipux, que entrega en un salto (ADR-0051).</item>
/// <item>La cola de placa la mueve el OT (asignar, liberar, rechazar); el envío al OT lo hace el gestor.</item>
/// <item>La presencia de placa debe ser coherente con el destino: sin placa no hay <c>asignado</c>; con
/// placa no se radica a <c>preasignacion</c>.</item>
/// <item>Re-radicar desde <c>rechazado</c> exige subsanación activa (ADR-0033).</item>
/// </list>
/// Las aristas anteriores a ADR-0059 se evalúan solo con la máquina: sus autorizaciones viven en los
/// endpoints y no se reproducen aquí.
/// </summary>
public static class TramiteTransitionPolicy
{
    /// <summary>
    /// Estado al que aterriza una RADICACIÓN (desde <c>preparado</c> o re-radicación desde
    /// <c>rechazado</c>): la Ruta Larga (tipo pide placa y no la tiene) entra por
    /// <see cref="TramiteEstado.Preasignacion"/>; todo lo demás por <see cref="TramiteEstado.Entregado"/>.
    /// Es el mismo predicado que <see cref="Evaluate"/> usa para vetar el destino contrario.
    /// </summary>
    public static string DestinoDeRadicacion(TransitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RadicaSinPlaca(context) ? TramiteEstado.Preasignacion : TramiteEstado.Entregado;
    }

    public static TramiteTransitionVerdict Evaluate(string? from, string? to, TransitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TramiteStateMachine.IsValidTransition(from, to))
            return Denegar(
                TramiteEstadoErrores.TransicionNoPermitida,
                $"La transición de '{from}' a '{to}' no está permitida.");

        if (TramiteEstado.EsEstadoDeRutaDePlaca(to) && !context.RequiresPlateRequest)
            return Denegar(
                TramiteEstadoErrores.TransicionRequierePlaca,
                $"El estado '{to}' solo aplica a tipos de trámite que piden placa.");

        if (from == TramiteEstado.Rechazado && EsReRadicacion(to) && !context.SubsanacionActiva)
            return Denegar(
                TramiteEstadoErrores.TransicionNoPermitida,
                "Solo se puede re-radicar cuando la subsanación está activa.");

        return (from, to) switch
        {
            (TramiteEstado.Preparado or TramiteEstado.Rechazado, TramiteEstado.Preasignacion) =>
                EvaluarRadicacionSinPlaca(context),
            (TramiteEstado.Preparado or TramiteEstado.Rechazado, TramiteEstado.Entregado) =>
                EvaluarRadicacionAEntregado(context),
            (TramiteEstado.Preasignacion, TramiteEstado.Asignado) => EvaluarAsignacionDePlaca(context),
            (TramiteEstado.Preasignacion, TramiteEstado.Rechazado) => SoloOt(context, "rechazar en preasignación"),
            (TramiteEstado.Asignado, TramiteEstado.Preasignacion) => SoloOt(context, "liberar la placa"),
            (TramiteEstado.Asignado, TramiteEstado.Entregado) => SoloGestor(context, "enviar al OT"),
            _ => TramiteTransitionVerdict.Permitida,
        };
    }

    private static bool EsReRadicacion(string? to) =>
        to is TramiteEstado.Entregado or TramiteEstado.Preasignacion;

    private static bool RadicaSinPlaca(TransitionContext context) =>
        context.RequiresPlateRequest && !context.HasPlate;

    private static TramiteTransitionVerdict EvaluarRadicacionSinPlaca(TransitionContext context)
    {
        var actor = SoloGestor(context, "radicar");
        if (!actor.Allowed)
            return actor;

        return context.HasPlate
            ? Denegar(
                TramiteEstadoErrores.TransicionPlacaIncoherente,
                "El trámite ya tiene placa: se radica directamente a 'entregado', no a 'preasignacion'.")
            : TramiteTransitionVerdict.Permitida;
    }

    private static TramiteTransitionVerdict EvaluarRadicacionAEntregado(TransitionContext context)
    {
        // ADR-0051 — Quipux entrega en un solo salto, pida placa el tipo o no.
        if (context.Actor == TramiteActor.Quipux)
            return TramiteTransitionVerdict.Permitida;

        return RadicaSinPlaca(context)
            ? Denegar(
                TramiteEstadoErrores.TransicionRequierePreasignacion,
                "El tipo pide placa y el trámite no la tiene: debe radicarse a 'preasignacion'.")
            : TramiteTransitionVerdict.Permitida;
    }

    private static TramiteTransitionVerdict EvaluarAsignacionDePlaca(TransitionContext context)
    {
        var actor = SoloOt(context, "asignar la placa");
        if (!actor.Allowed)
            return actor;

        return context.HasPlate
            ? TramiteTransitionVerdict.Permitida
            : Denegar(
                TramiteEstadoErrores.TransicionPlacaIncoherente,
                "No se puede pasar a 'asignado' sin una placa asignada.");
    }

    private static TramiteTransitionVerdict SoloOt(TransitionContext context, string accion) =>
        context.Actor == TramiteActor.Ot
            ? TramiteTransitionVerdict.Permitida
            : Denegar(
                TramiteEstadoErrores.TransicionSoloOt,
                $"Solo el Organismo de Tránsito puede {accion}.");

    private static TramiteTransitionVerdict SoloGestor(TransitionContext context, string accion) =>
        context.Actor is TramiteActor.Gestor or TramiteActor.Sistema
            ? TramiteTransitionVerdict.Permitida
            : Denegar(
                TramiteEstadoErrores.TransicionSoloGestor,
                $"Solo el gestor puede {accion}.");

    private static TramiteTransitionVerdict Denegar(string errorCode, string detail) =>
        TramiteTransitionVerdict.Denegada(errorCode, detail);
}
