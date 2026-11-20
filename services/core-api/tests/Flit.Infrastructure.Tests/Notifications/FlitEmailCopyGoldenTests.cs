using Flit.Analytics.Application.Dtos;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.OtRules;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tests.Shared;
using Flit.Tramites.Domain.RevocationRequests;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications;

/// <summary>
/// Rediseño «Correos 2.0» — guardia de copy de TODOS los correos FLIT de core-api: congela el
/// texto visible (asunto + cuerpo, sin marcado) con <see cref="EmailCopyGolden"/>. Los
/// <c>*.text.golden.txt</c> se generaron con el diseño ANTERIOR, antes de tocar un solo composer:
/// el rediseño cambia el marcado (los <c>*.body.golden.txt</c>), nunca estas palabras.
/// <para>
/// Además congela el HTML de los dos correos que no tenían golden: identidad (captura manual y su
/// rechazo, FLIT y marca) y la simulación de mandato. Los de marca NO deben moverse nunca.
/// </para>
/// </summary>
public sealed class FlitEmailCopyGoldenTests
{
    private const string AssetsBaseUrl = "https://cdn.flit.test/email-assets";

    private static readonly EmailTheme BrandTheme = new(
        EmailThemeKind.Brand,
        "Movilidad Andina",
        "https://dev.flitsas.online/api/v1/public/branding/logos/11111111-1111-4111-8111-111111111111",
        "#0B3D91",
        "#1FA2FF",
        "#FFFFFF",
        7);

    // ── Security (core-identity) ────────────────────────────────────────────────────────────

    [Fact]
    public void Invitacion() => Copy(SecurityEmailPreviewSample.BuildInvitation(AssetsBaseUrl), "copy-security-invitacion");

    [Fact]
    public void Recuperacion_contrasena() =>
        Copy(SecurityEmailPreviewSample.BuildForgotPassword(30, AssetsBaseUrl), "copy-security-recuperacion-contrasena");

    [Fact]
    public void Reset_administrativo() =>
        Copy(SecurityEmailPreviewSample.BuildAdminResetPassword(AssetsBaseUrl), "copy-security-reset-administrativo");

    [Fact]
    public void Bienvenida() =>
        Copy(SecurityEmailPreviewSample.BuildWelcomeRegistration(AssetsBaseUrl), "copy-security-bienvenida");

    // ── Trámites ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Tramite_aprobado() => Copy(TramiteEmailPreviewSample.BuildFlitAprobado(AssetsBaseUrl), "copy-tramites-aprobado");

    [Fact]
    public void Tramite_rechazado() => Copy(TramiteEmailPreviewSample.BuildFlitRechazado(AssetsBaseUrl), "copy-tramites-rechazado");

    [Fact]
    public void Asignacion_placa() => Copy(AsignacionPlacaEmailPreviewSample.BuildFlit(AssetsBaseUrl), "copy-tramites-asignacion-placa");

    [Theory]
    [InlineData(RevocationRequestEmailMilestone.Solicitada, "solicitada")]
    [InlineData(RevocationRequestEmailMilestone.Aprobada, "aprobada")]
    [InlineData(RevocationRequestEmailMilestone.Rechazada, "rechazada")]
    public void Revocatoria(string milestone, string slug) =>
        Copy(RevocationRequestEmailPreviewSample.BuildFlit(milestone, AssetsBaseUrl), $"copy-tramites-revocatoria-{slug}");

    // ── Identidad (captura manual) ──────────────────────────────────────────────────────────

    [Fact]
    public void Identidad_captura_manual()
    {
        var mail = ManualCaptureEmailPreviewSample.Build(AssetsBaseUrl);
        Copy(mail, "copy-identidad-captura-manual");
        Html(mail, "identidad-captura-manual-flit");
    }

    [Fact]
    public void Identidad_captura_manual_rechazo()
    {
        var mail = ManualCaptureRejectedEmailPreviewSample.Build(AssetsBaseUrl);
        Copy(mail, "copy-identidad-captura-manual-rechazo");
        Html(mail, "identidad-captura-manual-rechazo-flit");
    }

    [Fact]
    public void Identidad_captura_manual_brand_no_cambia() =>
        Html(ManualCaptureEmailPreviewSample.Build(AssetsBaseUrl, BrandTheme), "identidad-captura-manual-brand");

    [Fact]
    public void Identidad_captura_manual_rechazo_brand_no_cambia() =>
        Html(ManualCaptureRejectedEmailPreviewSample.Build(AssetsBaseUrl, BrandTheme), "identidad-captura-manual-rechazo-brand");

    // ── Reportes 2.0 (scheduler) ────────────────────────────────────────────────────────────

    [Fact]
    public void Informe_programado() => Copy(AnalyticsEmailPreviewSample.BuildScheduledReport(), "copy-analytics-informe-programado");

    [Fact]
    public void Informe_programado_sin_actividad() =>
        Copy(
            SchedulerEmailComposer.BuildScheduledReport(
                AnalyticsEmailPreviewSample.PhScheduleName, "uso", AnalyticsEmailPreviewSample.PhPeriodLabel,
                Array.Empty<CategoryMetricsDto>(), Array.Empty<TopProducerDto>()),
            "copy-analytics-informe-sin-actividad");

    [Fact]
    public void Alerta() => Copy(AnalyticsEmailPreviewSample.BuildAlert(), "copy-analytics-alerta");

    [Fact]
    public void Consulta_programada() =>
        Copy(
            SchedulerEmailComposer.BuildConsultaReport(
                AnalyticsEmailPreviewSample.PhScheduleName, "[ACÁ VA EL NOMBRE DE LA CONSULTA]", 1200, truncated: true, rowCap: 1000),
            "copy-analytics-consulta");

    [Fact]
    public void Consulta_programada_no_disponible() =>
        Copy(
            SchedulerEmailComposer.BuildConsultaReportMissing(AnalyticsEmailPreviewSample.PhScheduleName),
            "copy-analytics-consulta-no-disponible");

    // ── Simulación de mandato ───────────────────────────────────────────────────────────────

    [Fact]
    public void Simulacion_mandato()
    {
        var mail = MandateSimulatorEmail();
        Copy(mail, "copy-admin-mandato-simulacion");
        Html(mail, "admin-mandato-simulacion");
    }

    private static (string Subject, string Html) MandateSimulatorEmail() =>
        (MandateSimulatorService.BuildEmailSubject("Secretaría de Movilidad de Prueba"),
            MandateSimulatorService.BuildEmailHtml("Secretaría de Movilidad de Prueba", AssetsBaseUrl));

    private static void Copy((string Subject, string Html) mail, string plantilla) =>
        EmailCopyGolden.Assert(mail.Subject, mail.Html, plantilla);

    private static void Copy(Flit.Modules.Security.Application.Auth.ComposedEmail mail, string plantilla) =>
        EmailCopyGolden.Assert(mail.Subject, mail.HtmlBody, plantilla);

    private static void Html((string Subject, string Html) mail, string plantilla) =>
        EmailGolden.Assert(
            new EmailMessage(TenantId: null, plantilla, "destinatario@flit.test", "Destinatario De Prueba", mail.Subject, mail.Html),
            plantilla);
}
