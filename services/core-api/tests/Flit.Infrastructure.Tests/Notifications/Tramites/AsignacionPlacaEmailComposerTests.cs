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
        Ciudad: "Medellín",
        SecretariaTransito: "Secretaría de Movilidad de Medellín",
        EstadoActual: "Asignado");

    [Fact]
    public void ComposeFlit_IncluyeCuerpoYSoporteFlit()
    {
        var (subject, html) = AsignacionPlacaEmailComposer.ComposeFlit(
            Sample, AsignacionPlacaEmailPreviewSample.DefaultAssetsBaseUrl);

        subject.Should().Contain("ABC123").And.Contain("Asignado");
        html.Should().Contain("Juan Carlos P&#233;rez G&#243;mez");
        html.Should().Contain("ABC123");
        html.Should().Contain("Medell&#237;n");
        html.Should().Contain("Secretar&#237;a de Movilidad de Medell&#237;n");
        html.Should().Contain("Estado Actual:");
        html.Should().Contain("Asignado");
        html.Should().Contain("soporte@flitsas.com");
        html.Should().Contain("flit-logo.png");
        html.Should().NotContain("Renting Colombia");
        html.Should().NotContain("018000524444");
        html.Should().NotContain("Variante FLIT");
    }

    [Fact]
    public void ComposeRenting_IncluyeLogoContactosYSoporteRenting()
    {
        var (subject, html) = AsignacionPlacaEmailComposer.ComposeRenting(
            Sample, AsignacionPlacaEmailPreviewSample.DefaultAssetsBaseUrl);

        subject.Should().Contain("ABC123");
        html.Should().Contain("renting-colombia-logo.png");
        html.Should().Contain("flit-logo.png");
        html.Should().Contain("018000524444");
        html.Should().Contain("3508285539");
        html.Should().Contain("servicio@rentingcolombia.com");
        html.Should().Contain("dejanos-tus-comentarios-te-asesoramos");
        html.Should().NotContain("soporte@flitsas.com");
        html.Should().NotContain("Variante Renting");
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

        flit.Html.Should().Contain("soporte@flitsas.com");
        renting.Html.Should().Contain("servicio@rentingcolombia.com");
        flit.Html.Should().NotBe(renting.Html);
    }

    [Fact]
    public void RentingCompanyRegisteredNit_DocumentaReglaProductiva()
    {
        AsignacionPlacaEmailComposer.RentingCompanyRegisteredNit.Should().Be("811011779");
    }
}
