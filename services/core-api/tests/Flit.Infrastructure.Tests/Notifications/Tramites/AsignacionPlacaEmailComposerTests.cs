using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Admin.Domain.Companies.Settings;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

public class AsignacionPlacaEmailComposerTests
{
    private static readonly AsignacionPlacaEmailModel Sample = new(
        ClienteNombre: "Juan Carlos Pérez Gómez",
        Placa: "ABC123",
        EstadoActual: "Asignado",
        Ciudad: "Medellín",
        SecretariaTransito: "Secretaría de Movilidad de Medellín");

    [Fact]
    public void ComposeFlit_IncluyeHeaderLogoYSoporteFlit()
    {
        var (subject, html) = AsignacionPlacaEmailComposer.ComposeFlit(
            Sample, AsignacionPlacaEmailPreviewSample.DefaultAssetsBaseUrl);

        subject.Should().Contain("ABC123").And.Contain("Asignado").And.Contain("[FLIT]");
        html.Should().Contain("tramite-cambio-estado-header.png");
        html.Should().Contain("flit-logo.png");
        html.Should().Contain("Juan Carlos P&#233;rez G&#243;mez");
        html.Should().Contain("ABC123");
        html.Should().Contain("Estado Actual:");
        html.Should().Contain("Asignado");
        html.Should().Contain("soporte@flitsas.com");
        html.Should().NotContain("tramite-cambio-estado-renting-header.png");
        html.Should().NotContain("tramite-cambio-estado-renting-footer.png");
        html.Should().NotContain("018000524444");
    }

    [Fact]
    public void ComposeRenting_UsaHeaderFooterRentingSinLogosFlit()
    {
        var (subject, html) = AsignacionPlacaEmailComposer.ComposeRenting(
            Sample, AsignacionPlacaEmailPreviewSample.DefaultAssetsBaseUrl);

        subject.Should().Contain("ABC123");
        subject.Should().NotContain("[FLIT]");
        html.Should().Contain("tramite-cambio-estado-renting-header.png");
        html.Should().Contain("tramite-cambio-estado-renting-footer.png");
        html.Should().Contain("renting-header-bg");
        html.Should().Contain("renting-footer-bg");
        html.Should().Contain("018000524444");
        html.Should().Contain("3508285539");
        html.Should().Contain("servicio@rentingcolombia.com");
        html.Should().Contain("dejanos-tus-comentarios-te-asesoramos");
        html.Should().NotContain("flit-logo.png");
        html.Should().NotContain("tramite-cambio-estado-header.png");
        html.Should().NotContain("soporte@flitsas.com");
        html.Should().NotContain("alt=\"flit\"");
    }

    [Fact]
    public void Compose_EscapaHtmlEnCamposDinamicos()
    {
        var model = Sample with { ClienteNombre = "Ana <script>alert(1)</script>" };
        var (_, html) = AsignacionPlacaEmailComposer.ComposeFlit(
            model, AsignacionPlacaEmailPreviewSample.DefaultAssetsBaseUrl);

        html.Should().Contain("&lt;script&gt;");
        html.Should().NotContain("<script>alert(1)</script>");
    }

    [Fact]
    public void SampleRenderer_FlitVsRenting_DifierenPorCanal()
    {
        var flit = NotificationSampleRenderer.Render(
            AsignacionPlacaEmailComposer.TemplateId, NotificationChannel.FlitSmtp);
        var renting = NotificationSampleRenderer.Render(
            AsignacionPlacaEmailComposer.TemplateId, NotificationChannel.TenantApi);

        flit.Html.Should().Contain("flit-logo.png");
        renting.Html.Should().NotContain("flit-logo.png");
        renting.Html.Should().Contain("tramite-cambio-estado-renting-footer.png");
        flit.Html.Should().NotBe(renting.Html);
    }

    [Fact]
    public void RentingCompanyRegisteredNit_DocumentaReglaProductiva()
    {
        AsignacionPlacaEmailComposer.RentingCompanyRegisteredNit.Should().Be("811011779");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compose_CopyActualizado_PlacaAsignadaSinPosiblePlaca_IncluyeSoat(bool flit)
    {
        var (_, html) = flit
            ? AsignacionPlacaEmailComposer.ComposeFlit(
                Sample, AsignacionPlacaEmailPreviewSample.DefaultAssetsBaseUrl)
            : AsignacionPlacaEmailComposer.ComposeRenting(
                Sample, AsignacionPlacaEmailPreviewSample.DefaultAssetsBaseUrl);

        html.Should().Contain("placa asignada es");
        html.Should().Contain("SOAT (Seguro Obligatorio de Accidentes de Tránsito)");
        html.Should().NotContain("posible placa", "el copy ya no debe sugerir placa provisional");
        html.Should().NotContain("cuya posible placa es");
    }

    [Theory]
    [InlineData(NotificationChannel.FlitSmtp)]
    [InlineData(NotificationChannel.TenantApi)]
    public void SampleRenderer_AsignacionPlaca_RenderizaSinExcepcion_ConCopyNuevo(NotificationChannel channel)
    {
        var (subject, html) = NotificationSampleRenderer.Render(
            AsignacionPlacaEmailComposer.TemplateId, channel);

        subject.Should().Contain("ABC123");
        html.Should().Contain("placa asignada es");
        html.Should().Contain("SOAT (Seguro Obligatorio de Accidentes de Tránsito)");
        html.Should().NotContain("posible placa");
    }
}
