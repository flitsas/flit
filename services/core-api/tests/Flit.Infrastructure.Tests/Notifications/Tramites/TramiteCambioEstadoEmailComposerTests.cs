using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Tramites;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

public class TramiteCambioEstadoEmailComposerTests
{
    private static readonly TramiteCambioEstadoEmailModel TraspasoRechazado = new(
        "EL NEGOCIO DE AMADO",
        "BANCOLOMBIA S.A",
        "GSR101",
        "FUNZA",
        "STRIA TTOyTTE PAL FUNZA",
        "RECHAZADO",
        EsTraspaso: true);

    private static readonly TramiteCambioEstadoEmailModel TraspasoAprobado = new(
        "BANCOLOMBIA S.A",
        "EL NEGOCIO DEL ALVARO AMADO",
        "GSR101",
        "FUNZA",
        "STRIA TTOyTTE PAL FUNZA",
        "APROBADO",
        EsTraspaso: true);

    [Fact]
    public void ComposeFlit_Rechazado_IncluyeMarcaBannerLogoYEstado()
    {
        var (subject, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(
            TraspasoRechazado, "https://cdn.example/email-assets");

        subject.Should().Contain("GSR101").And.Contain("RECHAZADO");
        html.Should().Contain("https://cdn.example/email-assets/tramite-cambio-estado-header.png");
        html.Should().Contain("https://cdn.example/email-assets/flit-logo.png");
        html.Should().Contain("¡NOTIFICACIÓN RADICACIÓN DEL TRÁMITE!");
        html.Should().Contain("❌");
        html.Should().Contain("RECHAZADO");
        html.Should().Contain("POLÍTICA DE PRIVACIDAD");
    }

    [Fact]
    public void ComposeFlit_Aprobado_UsaCheckVerdeYPolitica()
    {
        var (subject, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(
            TraspasoAprobado, "https://cdn.example/email-assets");

        subject.Should().Contain("APROBADO");
        html.Should().Contain("APROBADO");
        html.Should().Contain("✓");
        html.Should().NotContain("❌");
        html.Should().Contain("BANCOLOMBIA S.A");
        html.Should().Contain("EL NEGOCIO DEL ALVARO AMADO");
        html.Should().Contain("POLÍTICA DE PRIVACIDAD");
    }

    [Fact]
    public void ComposeRenting_IncluyeHeaderFooterYCopyDelDiseno()
    {
        var (subject, html) = TramiteCambioEstadoEmailComposer.ComposeRenting(
            TraspasoRechazado, "https://cdn.example/email-assets");

        subject.Should().Contain("GSR101");
        html.Should().Contain("https://cdn.example/email-assets/tramite-cambio-estado-renting-header.png");
        html.Should().Contain("https://cdn.example/email-assets/tramite-cambio-estado-renting-footer.png");
        html.Should().Contain("¡Es un gusto saludarte!");
        html.Should().Contain("Detalles clave:");
        html.Should().Contain("renting-header-bg");
        html.Should().Contain("renting-footer-bg");
        html.Should().Contain("bgcolor=\"#000000\"");
        html.Should().Contain("bgcolor=\"#ffffff\"");
        html.Should().Contain("color-scheme");
    }

    [Fact]
    public void ComposeRenting_Aprobado_UsaBuenasNoticias()
    {
        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeRenting(
            TraspasoAprobado, "https://cdn.example/email-assets");

        html.Should().Contain("¡Buenas Noticias!");
        html.Should().Contain("APROBADO");
        html.Should().Contain("Tu tarjeta de propiedad/matrícula llegará pronto");
    }

    [Fact]
    public void SampleRenderer_AprobadoYRechazado_DifierenPorEstadoYCanal()
    {
        var aprobadoFlit = NotificationSampleRenderer.Render(
            TramiteCambioEstadoEmailComposer.TemplateIdAprobado,
            NotificationChannel.FlitSmtp,
            "https://cdn.example/email-assets");
        var rechazadoRenting = NotificationSampleRenderer.Render(
            TramiteCambioEstadoEmailComposer.TemplateIdRechazado,
            NotificationChannel.TenantApi,
            "https://cdn.example/email-assets");

        aprobadoFlit.Html.Should().Contain("APROBADO");
        aprobadoFlit.Html.Should().Contain("tramite-cambio-estado-header.png");
        rechazadoRenting.Html.Should().Contain("RECHAZADO");
        rechazadoRenting.Html.Should().Contain("tramite-cambio-estado-renting-header.png");
        aprobadoFlit.Html.Should().NotBe(rechazadoRenting.Html);
    }

    [Fact]
    public void PreviewSample_BuildFlitAprobado_UsaAssetsBaseUrl()
    {
        var (_, html) = TramiteEmailPreviewSample.BuildFlitAprobado("http://localhost:3000/email-assets");
        html.Should().Contain("http://localhost:3000/email-assets/tramite-cambio-estado-header.png");
        html.Should().Contain("APROBADO");
    }

    [Fact]
    public void PreviewSample_BuildRentingRechazado_UsaAssetsBaseUrl()
    {
        var (_, html) = TramiteEmailPreviewSample.BuildRentingRechazado("http://localhost:3000/email-assets");
        html.Should().Contain("http://localhost:3000/email-assets/tramite-cambio-estado-renting-header.png");
        html.Should().Contain("RECHAZADO");
    }
}
