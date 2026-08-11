namespace Flit.Infrastructure.Notifications.Catalog;

/// <summary>
/// Catálogo enumerable de las plantillas de correo de la plataforma (HU #11353, Feature #11347).
/// </summary>
/// <remarks>
/// <para>
/// Vive en <c>Flit.Infrastructure</c> por decisión de Líder Técnico (no reabrir): es el único
/// proyecto que ve las 5 plantillas — <c>Flit.Admin.Application</c> no referencia ni a
/// <c>Security.Application</c> ni a <c>Analytics.Application</c>, y
/// <c>SchedulerEmailComposer</c> es <c>internal</c>. El corte fija la DIRECCIÓN de la
/// dependencia, no la ubicación: este catálogo CONSUME a los composers de cada módulo; ningún
/// composer ni handler consume a este catálogo.
/// </para>
/// <para>
/// AC4 (alta sin tocar consumidores): registrar una plantilla nueva es agregar UNA entrada al
/// arreglo <see cref="All"/> — <see cref="TryResolve(string, out NotificationTemplateDescriptor)"/> y
/// cualquier código de la pareja (plantilla, canal) siguen intactos.
/// </para>
/// </remarks>
public static class NotificationTemplateCatalog
{
    /// <summary>
    /// AC3 — ids ESTABLES, literales escritos a mano. Nunca <c>nameof</c>/<c>typeof(...).Name</c>:
    /// renombrar <c>InvitationEmailTemplate</c> a otra cosa no cambia <c>"security.invitation"</c>.
    /// </summary>
    private static class TemplateIds
    {
        public const string Invitation = "security.invitation";
        public const string ForgotPassword = "security.forgot-password";
        public const string AdminResetPassword = "security.admin-reset-password";
        public const string ScheduledReport = "analytics.scheduled-report";
        public const string Alert = "analytics.alert";
    }

    /// <summary>
    /// AC1 — exactamente 5 entradas. AC2 — invitación declara los DOS disparadores que comparten
    /// la plantilla (crear invitación y reenviar invitación): 5 plantillas, 6 disparadores.
    /// </summary>
    public static IReadOnlyList<NotificationTemplateDescriptor> All { get; } =
    [
        new NotificationTemplateDescriptor(
            TemplateIds.Invitation,
            "Invitación a la plataforma",
            NotificationModule.Security,
            [NotificationTrigger.CreateInvitation, NotificationTrigger.ResendInvitation]),
        new NotificationTemplateDescriptor(
            TemplateIds.ForgotPassword,
            "Recuperación de contraseña",
            NotificationModule.Security,
            [NotificationTrigger.ForgotPassword]),
        new NotificationTemplateDescriptor(
            TemplateIds.AdminResetPassword,
            "Reset administrativo de contraseña",
            NotificationModule.Security,
            [NotificationTrigger.AdminResetPassword]),
        new NotificationTemplateDescriptor(
            TemplateIds.ScheduledReport,
            "Informe programado",
            NotificationModule.Analytics,
            [NotificationTrigger.ScheduledReport]),
        new NotificationTemplateDescriptor(
            TemplateIds.Alert,
            "Alerta de analítica",
            NotificationModule.Analytics,
            [NotificationTrigger.Alert]),
    ];

    /// <summary>
    /// AC5 — resuelve por id; sin plantilla por defecto. <c>false</c> si el id no corresponde a
    /// ninguna entrada del catálogo.
    /// </summary>
    public static bool TryResolve(string templateId, out NotificationTemplateDescriptor descriptor)
    {
        foreach (var entry in All)
        {
            if (string.Equals(entry.Id, templateId, StringComparison.Ordinal))
            {
                descriptor = entry;
                return true;
            }
        }

        descriptor = null!;
        return false;
    }

    /// <summary>
    /// Resuelve por la pareja (plantilla, canal). El canal es genérico — ver
    /// <see cref="NotificationTemplateKey{TChannel}"/> — y NUNCA se inspecciona aquí: no existe
    /// lista fija por canal, así que dar de alta un canal nuevo no toca este método. Sin
    /// plantilla por defecto (AC5): igual que <see cref="TryResolve(string, out NotificationTemplateDescriptor)"/>.
    /// </summary>
    public static bool TryResolve<TChannel>(
        NotificationTemplateKey<TChannel> key, out NotificationTemplateDescriptor descriptor) =>
        TryResolve(key.TemplateId, out descriptor);
}
