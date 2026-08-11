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

    /// <summary>Reportes 2.0 HU-D — <c>AnalyticsSchedulerProcessor</c> (informe programado).</summary>
    ScheduledReport,

    /// <summary>Reportes 2.0 HU-D — <c>AnalyticsSchedulerProcessor</c> (alerta de métrica).</summary>
    Alert,
}
