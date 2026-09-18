using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// Uso de ejemplo:
/// var html = BrandedEmailChrome.Wrap(theme, "Título", "&lt;p&gt;cuerpo&lt;/p&gt;");
/// HU #12428 AC2/AC3/AC6/AC7 — estructura fija de encabezado/pie sobre un tema de marca: sin HTML
/// libre, colores validados, 600 px, `alt` en toda imagen.
/// </summary>
public sealed class BrandedEmailChromeTests
{
    private static EmailTheme BrandTheme(
        string platformName = "Movilidad Andina",
        string? logoUrl = "https://dev.flitsas.online/api/v1/public/branding/logos/abc",
        string primary = "#0B3D91") =>
        new(EmailThemeKind.Brand, platformName, logoUrl, primary, "#1FA2FF", "#FFFFFF", 3);

    [Fact]
    public void AC6_NombreConMarcado_QuedaEscapadoNuncaSeInterpretaComoHtml()
    {
        var theme = BrandTheme(platformName: "<script>alert(1)</script>");

        var html = BrandedEmailChrome.Wrap(theme, "Título", "<p>cuerpo</p>");

        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void AC6_ColorFueraDeFormatoHex_CaeAlColorDeRespaldoFlit()
    {
        var theme = BrandTheme(primary: "javascript:alert(1)");

        var html = BrandedEmailChrome.Wrap(theme, "Título", "<p>cuerpo</p>");

        html.Should().NotContain("javascript:alert(1)");
        html.Should().Contain(EmailTheme.Flit.Primary);
    }

    [Fact]
    public void AC3_LogoUrlAbsolutaHttps_SeUsaComoSrcConAltDelNombreDeLaMarca()
    {
        var theme = BrandTheme();

        var html = BrandedEmailChrome.Wrap(theme, "Título", "<p>cuerpo</p>");

        html.Should().Contain($"src=\"{theme.LogoUrl}\"");
        html.Should().Contain($"alt=\"{theme.PlatformName}\"");
    }

    [Fact]
    public void AC2_SinLogoUrl_MuestraElNombreDeLaMarcaEnTextoSinDejarElEncabezadoVacio()
    {
        var theme = BrandTheme(logoUrl: null);

        var html = BrandedEmailChrome.Wrap(theme, "Título", "<p>cuerpo</p>");

        html.Should().NotContain("<img");
        html.Should().Contain(theme.PlatformName);
    }

    [Fact]
    public void AC3_LogoUrlNoAbsolutaOJavascript_SeIgnoraYCaeAlTextoDeLaMarca()
    {
        var theme = BrandTheme(logoUrl: "javascript:alert(1)");

        var html = BrandedEmailChrome.Wrap(theme, "Título", "<p>cuerpo</p>");

        html.Should().NotContain("<img");
        html.Should().NotContain("javascript:alert(1)");
    }

    [Fact]
    public void AC7_TablaDe600PxSinFuentesExternasNiEstilosFueraDeLinea()
    {
        var theme = BrandTheme();

        var html = BrandedEmailChrome.Wrap(theme, "Título", "<p>cuerpo</p>");

        html.Should().Contain("max-width:600px");
        html.Should().NotContain("<link");
        html.Should().NotContain("<style");
        html.Should().NotContain("@import");
        html.Should().NotContain("googleapis.com");
    }

    [Fact]
    public void AC2_PieContieneElNombreDeLaMarca()
    {
        var theme = BrandTheme();

        var html = BrandedEmailChrome.Wrap(theme, "Título", "<p>cuerpo</p>");

        html.Should().Contain(theme.PlatformName);
        html.Should().Contain("no respondas a este correo");
    }

    [Fact]
    public void AC2_ClosingHeadlineOpcional_SeIncluyeCuandoViene()
    {
        var theme = BrandTheme();

        var html = BrandedEmailChrome.Wrap(theme, "Título", "<p>cuerpo</p>", "Cierre destacado");

        html.Should().Contain("Cierre destacado");
    }

    [Fact]
    public void LinkColor_ConColorValido_LoDevuelveTalCual()
    {
        var theme = BrandTheme(primary: "#123ABC");

        BrandedEmailChrome.LinkColor(theme).Should().Be("#123ABC");
    }

    [Fact]
    public void LinkColor_ConColorInvalido_DevuelveElDeRespaldoFlit()
    {
        var theme = BrandTheme(primary: "not-a-color");

        BrandedEmailChrome.LinkColor(theme).Should().Be(EmailTheme.Flit.Primary);
    }
}
