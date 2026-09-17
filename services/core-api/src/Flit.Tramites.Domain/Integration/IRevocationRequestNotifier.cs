namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// HU #12572 (Feature #12565, AC3) — "solicitud de revocatoria recibida". Evento ORTOGONAL a
/// <see cref="ProcedureStateChangeEvent"/> (ADR-0022/ADR-0046): NO es un cambio de <c>status</c> del
/// trámite —permanece <c>aprobado</c> durante todo el sub-flujo de revocatoria (ver
/// <c>RevocationRequests.ProcedureRevocationRequest</c>, HU #12570/#12571)—, así que no puede viajar
/// por <see cref="IProcedureStateChangeNotifier"/> ni fabricar una fila sintética en su outbox: mismo
/// motivo que documentó ADR-0046 §Contexto punto 2 para el sub-estado de placa (contaminaría el
/// fan-out hacia los webhooks OT/ICT con un cambio de estado que no ocurrió).
/// </summary>
public sealed record RevocationRequestSolicitadaEvent(
    Guid TenantId,
    Guid ProcedureInstanceId,
    Guid RevocationRequestId,
    int AttemptNumber,
    Guid RequestedByUserId,
    DateTimeOffset RequestedAt);

/// <summary>
/// HU #12576 (Feature #12565, AC4) — "decisión de revocatoria registrada" (aprobada o rechazada por el
/// OT). Evento HERMANO de <see cref="RevocationRequestSolicitadaEvent"/>, mismo criterio ORTOGONAL
/// (ADR-0022/ADR-0046): ni la aprobación ni el rechazo de la SOLICITUD son, en sí mismos, un cambio de
/// <c>status</c> del trámite. Cuando la decisión es aprobar, la transición real Aprobado→Revocado la
/// dispara aparte <c>RevokeOtClientProcedureHandler</c> (reutilizado de HU #12166), que tiene su PROPIO
/// outbox de cambio de estado — este evento es solo el acuse de la DECISIÓN sobre la solicitud, no lo
/// sustituye.
/// </summary>
public sealed record RevocationRequestDecidedEvent(
    Guid TenantId,
    Guid ProcedureInstanceId,
    Guid RevocationRequestId,
    int AttemptNumber,
    bool Approved,
    Guid RequestedByUserId,
    Guid? DecidedBy,
    DateTimeOffset DecidedAt);

/// <summary>
/// Puerto de notificación del sub-flujo de revocatoria (HU #12572/#12576, Feature #12565). El endpoint
/// (capa Application/Api) lo invoca DESPUÉS de confirmar la creación/decisión de la fila de solicitud,
/// envuelto en <c>try/catch</c> — best-effort, mismo criterio que ADR-0046 Decisión (Opción B): un
/// fallo de notificación NUNCA revierte ni tumba la solicitud/decisión ya persistida ni afecta la
/// respuesta del endpoint. La resolución de destinatarios vía <c>ITramiteNotificationRecipientResolver</c>
/// —SIN modificarlo— y el encolado concreto del aviso viven en la implementación de Infrastructure
/// (<c>RevocationRequestNotificationEnqueuer</c>).
/// </summary>
public interface IRevocationRequestNotifier
{
    Task NotifyAsync(RevocationRequestSolicitadaEvent evt, CancellationToken cancellationToken = default);

    /// <summary>HU #12576 (AC4) — ver <see cref="RevocationRequestDecidedEvent"/>.</summary>
    Task NotifyDecisionAsync(RevocationRequestDecidedEvent evt, CancellationToken cancellationToken = default);
}

/// <summary>Implementación nula (tests / fallback si no hay sink registrado).</summary>
public sealed class NullRevocationRequestNotifier : IRevocationRequestNotifier
{
    public static NullRevocationRequestNotifier Instance { get; } = new();

    public Task NotifyAsync(RevocationRequestSolicitadaEvent evt, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyDecisionAsync(RevocationRequestDecidedEvent evt, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
