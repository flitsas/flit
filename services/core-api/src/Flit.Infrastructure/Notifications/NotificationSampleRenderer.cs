using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Notifications;

/// <summary>Overlay de catálogo para el render de muestra de aprobado/rechazado.</summary>
public sealed record NotificationSampleProcedureType(string Name, bool EsTraspaso);

/// <summary>
/// Despacho único del render de muestra del banco de pruebas (plantillas + variante por canal
/// cuando aplica). Consumido por el endpoint de muestra y por el envío de prueba.
/// </summary>
public static class NotificationSampleRenderer
{
    /// <param name="theme">
    /// HU #12428/#12431 — tema a aplicar sobre la muestra (<c>?tenantId=</c> del endpoint de
    /// plantillas, o el borrador/publicado de <c>GET /company/branding/email-sample</c>). <c>null</c>
    /// o <see cref="EmailThemeKind.Flit"/> ⇒ la muestra sale exactamente igual que antes de esta
    /// historia. El canal Renting (TenantApi) NUNCA recibe tema de marca (mismo criterio que
    /// producción, AC8).
    /// </param>
    public static (string Subject, string Html) Render(
        string templateId,
        NotificationChannel channel,
        string? assetsBaseUrl = null,
        NotificationSampleProcedureType? procedureType = null,
        EmailTheme? theme = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(assetsBaseUrl)
            ? TramiteEmailPreviewSample.DefaultAssetsBaseUrl
            : assetsBaseUrl;
        var effectiveTheme = channel == NotificationChannel.TenantApi ? null : theme;

        return templateId switch
        {
            "security.invitation" => ToTuple(SecurityEmailPreviewSample.BuildInvitation(baseUrl, effectiveTheme)),
            "security.forgot-password" => ToTuple(SecurityEmailPreviewSample.BuildForgotPassword(assetsBaseUrl: baseUrl, theme: effectiveTheme)),
            "security.admin-reset-password" => ToTuple(SecurityEmailPreviewSample.BuildAdminResetPassword(baseUrl, effectiveTheme)),
            "security.welcome-registration" => ToTuple(SecurityEmailPreviewSample.BuildWelcomeRegistration(baseUrl, effectiveTheme)),
            "analytics.scheduled-report" => AnalyticsEmailPreviewSample.BuildScheduledReport(effectiveTheme),
            "analytics.alert" => AnalyticsEmailPreviewSample.BuildAlert(effectiveTheme),
            TramiteCambioEstadoEmailComposer.TemplateIdAprobado => ComposeTramiteCambio(
                TramiteEmailPreviewSample.SampleAprobado, channel, baseUrl, procedureType, effectiveTheme),
            TramiteCambioEstadoEmailComposer.TemplateIdRechazado => ComposeTramiteCambio(
                TramiteEmailPreviewSample.SampleRechazado, channel, baseUrl, procedureType, effectiveTheme),
            AsignacionPlacaEmailComposer.TemplateId => channel == NotificationChannel.TenantApi
                ? AsignacionPlacaEmailPreviewSample.BuildRenting(baseUrl)
                : AsignacionPlacaEmailPreviewSample.BuildFlit(baseUrl, effectiveTheme),
            _ => throw new InvalidOperationException(
                $"El catálogo resolvió el id '{templateId}' pero no hay muestra registrada para él."),
        };
    }

    private static (string Subject, string Html) ComposeTramiteCambio(
        TramiteCambioEstadoEmailModel sample,
        NotificationChannel channel,
        string baseUrl,
        NotificationSampleProcedureType? procedureType,
        EmailTheme? theme)
    {
        var model = procedureType is null
            ? sample
            : TramiteEmailPreviewSample.OverlayProcedureType(
                sample, procedureType.Name, procedureType.EsTraspaso);

        return channel == NotificationChannel.TenantApi
            ? TramiteCambioEstadoEmailComposer.ComposeRenting(model, baseUrl)
            : TramiteCambioEstadoEmailComposer.ComposeFlit(model, baseUrl, theme);
    }


    private static (string Subject, string Html) ToTuple(
        Flit.Modules.Security.Application.Auth.ComposedEmail email) =>
        (email.Subject, email.HtmlBody);
}
