using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Tramites;

namespace Flit.Infrastructure.Notifications;

/// <summary>Overlay de catálogo para el render de muestra de aprobado/rechazado.</summary>
public sealed record NotificationSampleProcedureType(string Name, bool EsTraspaso);

/// <summary>
/// Despacho único del render de muestra del banco de pruebas (plantillas + variante por canal
/// cuando aplica). Consumido por el endpoint de muestra y por el envío de prueba.
/// </summary>
public static class NotificationSampleRenderer
{
    public static (string Subject, string Html) Render(
        string templateId,
        NotificationChannel channel,
        string? assetsBaseUrl = null,
        NotificationSampleProcedureType? procedureType = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(assetsBaseUrl)
            ? TramiteEmailPreviewSample.DefaultAssetsBaseUrl
            : assetsBaseUrl;

        return templateId switch
        {
            "security.invitation" => ToTuple(SecurityEmailPreviewSample.BuildInvitation(baseUrl)),
            "security.forgot-password" => ToTuple(SecurityEmailPreviewSample.BuildForgotPassword(assetsBaseUrl: baseUrl)),
            "security.admin-reset-password" => ToTuple(SecurityEmailPreviewSample.BuildAdminResetPassword(baseUrl)),
            "security.welcome-registration" => ToTuple(SecurityEmailPreviewSample.BuildWelcomeRegistration(baseUrl)),
            "analytics.scheduled-report" => AnalyticsEmailPreviewSample.BuildScheduledReport(),
            "analytics.alert" => AnalyticsEmailPreviewSample.BuildAlert(),
            TramiteCambioEstadoEmailComposer.TemplateIdAprobado => ComposeTramiteCambio(
                TramiteEmailPreviewSample.SampleAprobado, channel, baseUrl, procedureType),
            TramiteCambioEstadoEmailComposer.TemplateIdRechazado => ComposeTramiteCambio(
                TramiteEmailPreviewSample.SampleRechazado, channel, baseUrl, procedureType),
            AsignacionPlacaEmailComposer.TemplateId => channel == NotificationChannel.TenantApi
                ? AsignacionPlacaEmailPreviewSample.BuildRenting(baseUrl)
                : AsignacionPlacaEmailPreviewSample.BuildFlit(baseUrl),
            _ => throw new InvalidOperationException(
                $"El catálogo resolvió el id '{templateId}' pero no hay muestra registrada para él."),
        };
    }

    private static (string Subject, string Html) ComposeTramiteCambio(
        TramiteCambioEstadoEmailModel sample,
        NotificationChannel channel,
        string baseUrl,
        NotificationSampleProcedureType? procedureType)
    {
        var model = procedureType is null
            ? sample
            : TramiteEmailPreviewSample.OverlayProcedureType(
                sample, procedureType.Name, procedureType.EsTraspaso);

        return channel == NotificationChannel.TenantApi
            ? TramiteCambioEstadoEmailComposer.ComposeRenting(model, baseUrl)
            : TramiteCambioEstadoEmailComposer.ComposeFlit(model, baseUrl);
    }

    private static (string Subject, string Html) ToTuple(
        Flit.Modules.Security.Application.Auth.ComposedEmail email) =>
        (email.Subject, email.HtmlBody);
}
