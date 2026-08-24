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
        html.Should().NotContain("Motivo de rechazo");
        html.Should().NotContain(">Observación");
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
        html.Should().Contain("bgcolor=\"#ffffff\"");
        html.Should().Contain("content=\"light\"");
        html.Should().NotContain("bgcolor=\"#000000\"");
        html.Should().NotContain("light dark");
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
    public void ComposeFlit_MatriculaInicial_OmiteFilaVendedor()
    {
        var model = new TramiteCambioEstadoEmailModel(
            VendedorNombre: string.Empty,
            CompradorNombre: "Comprador SAS",
            Placa: "MI001",
            CiudadOt: "Medellín",
            NombreOt: "OT Medellín",
            EstadoActual: "APROBADO",
            EsTraspaso: false);

        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(model, "https://cdn.example/email-assets");

        html.Should().NotContain("<strong style=\"color:#2F6FED;\">Vendedor:</strong>");
        html.Should().Contain("Comprador SAS");
    }

    [Fact]
    public void ComposeRenting_MatriculaInicial_OmiteFilaVendedor()
    {
        var model = new TramiteCambioEstadoEmailModel(
            VendedorNombre: string.Empty,
            CompradorNombre: "Comprador SAS",
            Placa: "MI001",
            CiudadOt: "Medellín",
            NombreOt: "OT Medellín",
            EstadoActual: "APROBADO",
            EsTraspaso: false);

        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeRenting(model, "https://cdn.example/email-assets");

        html.Should().NotContain("<strong>Vendedor:</strong>");
        html.Should().Contain("Comprador SAS");
    }

    [Fact]
    public void ComposeFlit_ConNombreTipoTramite_InterpolaTalCualYNoUsaFallback()
    {
        var model = TraspasoAprobado with { NombreTipoTramite = "Cambio de color" };
        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(model, "https://cdn.example/email-assets");

        html.Should().Contain("el Cambio de color del vehículo");
        html.Should().NotContain("el traspaso de propiedad");
    }

    [Fact]
    public void ComposeFlit_SinCiudad_OmiteFragmento()
    {
        var model = TraspasoAprobado with { CiudadOt = string.Empty };
        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(model, "https://cdn.example/email-assets");

        html.Should().NotContain("<strong style=\"color:#2F6FED;\">Ciudad:</strong>");
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
        html.Should().Contain("Motivo de rechazo");
        html.Should().Contain("Observación");
    }

    [Fact]
    public void ComposeFlit_Rechazado_IncluyeCausalesYObservacion()
    {
        var model = TraspasoRechazado with
        {
            CausalesRechazo = ["Improntas no coinciden", "Documentos ilegibles"],
            ObservacionRechazo = "Adjuntar SOAT vigente.",
        };

        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(
            model, "https://cdn.example/email-assets");

        html.Should().Contain("Motivo de rechazo");
        html.Should().Contain("Improntas no coinciden; Documentos ilegibles");
        html.Should().Contain("Observación");
        html.Should().Contain("Adjuntar SOAT vigente.");
    }

    [Fact]
    public void ComposeRenting_Rechazado_IncluyeCausalesYObservacion()
    {
        var model = TraspasoRechazado with
        {
            CausalesRechazo = ["Documentos ilegibles"],
            ObservacionRechazo = "Falta el certificado de tradición.",
        };

        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeRenting(
            model, "https://cdn.example/email-assets");

        html.Should().Contain("<strong>Motivo de rechazo:</strong> Documentos ilegibles");
        html.Should().Contain("<strong>Observación:</strong> Falta el certificado de tradición.");
    }

    [Fact]
    public void ComposeFlit_Rechazado_SoloObservacion_OmiteMotivo()
    {
        var model = TraspasoRechazado with { ObservacionRechazo = "Rechazo de secretaría." };

        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(
            model, "https://cdn.example/email-assets");

        html.Should().NotContain("Motivo de rechazo");
        html.Should().Contain("Observación");
        html.Should().Contain("Rechazo de secretaría.");
    }

    [Fact]
    public void ComposeFlit_Rechazado_EscapaHtmlYConvierteSaltosDeLinea()
    {
        var model = TraspasoRechazado with
        {
            CausalesRechazo = ["<script>alert(1)</script>"],
            ObservacionRechazo = "Línea 1\nLínea 2",
        };

        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(
            model, "https://cdn.example/email-assets");

        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("Línea 1<br/>Línea 2");
    }

    [Fact]
    public void ComposeFlit_RechazadoSinDatos_OmiteBloque()
    {
        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(
            TraspasoRechazado, "https://cdn.example/email-assets");

        html.Should().NotContain("Motivo de rechazo");
        html.Should().NotContain(">Observación");
    }

    // --- Bug nov. 26: correo de soporte errado (soporte@flit.com → soporte@flitsas.com) ----

    [Fact]
    public void ComposeFlit_UsaCorreoDeSoporteFlitsas()
    {
        var (_, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(
            TraspasoAprobado, "https://cdn.example/email-assets");

        html.Should().Contain("soporte@flitsas.com");
        html.Should().NotContain("soporte@flit.com");
    }
}
