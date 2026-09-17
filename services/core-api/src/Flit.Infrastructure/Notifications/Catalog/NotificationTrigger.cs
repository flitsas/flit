namespace Flit.Infrastructure.Notifications.Catalog;

/// <summary>
/// Evento de negocio que dispara el envío de una plantilla (HU #11353 — AC2). Una plantilla puede
/// declarar más de un disparador: la de invitación cubre tanto crear invitación como reenviar
/// invitación pendiente, porque ambos handlers componen el mismo <c>InvitationEmailTemplate</c>.
/// </summary>
public enum NotificationTrigger
{
    /// <summary>HU #10175 — <c>CreateInvitationHandler</c>.</summary>
    CreateInvitation,

    /// <summary>HU #10625 — <c>ResendInvitationHandler</c>.</summary>
    ResendInvitation,

    /// <summary>HU #10169 — <c>ForgotPasswordHandler</c>.</summary>
    ForgotPassword,

    /// <summary>HU #10170 — <c>AdminResetPasswordHandler</c>.</summary>
    AdminResetPassword,

    /// <summary>HU #11489 H2 — <c>ActivateAccountHandler</c> tras activación exitosa.</summary>
    WelcomeRegistration,

    /// <summary>Reportes 2.0 HU-D — <c>AnalyticsSchedulerProcessor</c> (informe programado).</summary>
    ScheduledReport,

    /// <summary>Reportes 2.0 HU-D — <c>AnalyticsSchedulerProcessor</c> (alerta de métrica).</summary>
    Alert,

    /// <summary>
    /// Cambio de estado de un trámite (aprobado / rechazado). Declarativo en el catálogo del banco
    /// de pruebas; el handler productivo se conecta en una fase posterior.
    /// </summary>
    ProcedureStatusChanged,

    /// <summary>
    /// Asignación de placa en matrícula inicial (plantilla <c>tramites.asignacion-placa</c>).
    /// HU #11485 — encolado tras POST assign-plate (Flujo B, arista preasignado→asignado).
    /// </summary>
    PlateAssigned,

    /// <summary>
    /// Solicitud de revocatoria recibida (plantilla <c>tramites.revocatoria-solicitada</c>).
    /// HU #12579 (Feature #12565) — encolado por <c>RevocationRequestNotificationEnqueuer.NotifyAsync</c>.
    /// </summary>
    RevocationRequestSolicitada,

    /// <summary>
    /// Decisión de revocatoria: aprobada (plantilla <c>tramites.revocatoria-aprobada</c>).
    /// HU #12579 (Feature #12565) — encolado por <c>RevocationRequestNotificationEnqueuer.NotifyDecisionAsync</c>.
    /// </summary>
    RevocationRequestAprobada,

    /// <summary>
    /// Decisión de revocatoria: rechazada (plantilla <c>tramites.revocatoria-rechazada</c>).
    /// HU #12579 (Feature #12565) — encolado por <c>RevocationRequestNotificationEnqueuer.NotifyDecisionAsync</c>.
    /// </summary>
    RevocationRequestRechazada,
}
