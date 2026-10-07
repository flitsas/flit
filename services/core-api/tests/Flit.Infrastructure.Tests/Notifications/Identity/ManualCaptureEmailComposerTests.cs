using System.Net;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Identity;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Identity;

/// <summary>
/// HU #13287 (Feature #13280 A5, Épica #13202) — diseño del correo del enlace de captura manual
/// (<see cref="ManualCaptureEmailComposer"/> sobre <see cref="IdentityVerificationEmailLayout"/>): contenido fijo, ausencia total de la
/// palabra «Kyverum», escape del titular y del producto, tema de marca blanca y una sola fuente de HTML con la previsualización.
/// <para>Uso: <c>var (asunto, html) = ManualCaptureEmailComposer.Compose(nombre, enlace, EmailTheme.Flit, urlAssets);</c></para>
/// </summary>
public sealed class ManualCaptureEmailComposerTests
{
    private const string Assets = "https://app.flit.example/email-assets";
    private const string Link = "https://app.flit.example/captura-manual/tok_ABC";

    private static readonly EmailTheme Brand = new(
        EmailThemeKind.Brand, "Movilidad Andina", "https://dev.flit.example/api/v1/public/branding/logos/11111111-1111-4111-8111-111111111111",
        "#123ABC", "#456DEF", "#FFFFFF", 3);

    public static TheoryData<string> Temas => new() { "flit", "brand" };

    private static EmailTheme ThemeOf(string kind) => kind == "brand" ? Brand : EmailTheme.Flit;

    [Theory]
    [MemberData(nameof(Temas))]
    public void ElCorreoRenderizado_NuncaContieneLaPalabraKyverum(string tema)
    {
        var (subject, html) = ManualCaptureEmailComposer.Compose("Ana Perez", Link, ThemeOf(tema), Assets);

        subject.Should().NotContainEquivalentOf("kyverum");
        html.Should().NotContainEquivalentOf("kyverum");
        WebUtility.HtmlDecode(html).Should().NotContainEquivalentOf("kyverum");
    }

    [Fact]
    public void LaMuestraDelModuloDeNotificaciones_NoContieneKyverum_NiNumeroDeDocumento()
    {
        var (subject, html) = NotificationSampleRenderer.Render("identidad.captura-manual", NotificationChannel.FlitSmtp, Assets);

        (subject + html).Should().NotContainEquivalentOf("kyverum");
        html.Should().NotMatchRegex(@"(?<![#\w])\d{6,}(?!\w)", "no debe haber números largos tipo documento (los colores #RRGGBB no cuentan)");
    }

    [Fact]
    public void Flit_TieneAsuntoLogoEtiquetaPasosYBoton()
    {
        var (subject, html) = ManualCaptureEmailComposer.Compose("Ana Perez", Link, EmailTheme.Flit, Assets);
        var text = WebUtility.HtmlDecode(html);

        subject.Should().Be("[FLIT 2.0] Verifica tu identidad");
        html.Should().Contain($"src=\"{Assets}/flit-logo.png\"").And.Contain("alt=\"FLIT 2.0\"");
        html.Should().Contain("max-width:520px").And.Contain("border-radius:16px").And.Contain("#F8F8F4");
        text.Should().Contain("Verificación de identidad");
        html.Should().Contain("<h1").And.Contain("Verifica tu identidad").And.Contain("Hola Ana Perez.");
        text.IndexOf("Valida tus datos", StringComparison.Ordinal).Should()
            .BeLessThan(text.IndexOf("Realiza la validación biométrica", StringComparison.Ordinal));
        text.IndexOf("Captura los datos del documento", StringComparison.Ordinal).Should()
            .BeLessThan(text.IndexOf("Firma", text.IndexOf("Captura los datos", StringComparison.Ordinal), StringComparison.Ordinal));
        html.Should().Contain($"<a href=\"{Link}\"").And.Contain("background:#557EFF");
        html.Split($"href=\"{Link}\"").Length.Should().Be(3, "botón + enlace de respaldo en texto");
    }

    [Fact]
    public void ElNombreDelTitular_YElProducto_SeEscapan()
    {
        var theme = Brand with { PlatformName = "Red <b>\"X\"</b> & Cía" };

        var (subject, html) = ManualCaptureEmailComposer.Compose("<script>alert(1)</script>", Link, theme, Assets);

        html.Should().NotContain("<script>").And.NotContain("<b>").And.Contain("&lt;script&gt;");
        html.Should().Contain("Red &lt;b&gt;&quot;X&quot;&lt;/b&gt; &amp; C");
        subject.Should().Contain("Red <b>\"X\"</b> & Cía", "el asunto es texto plano, no HTML");
    }

    [Fact]
    public void MarcaBlanca_UsaLogoNombreYColorDeLaRed_ConElMismoEsqueleto()
    {
        var (subject, html) = ManualCaptureEmailComposer.Compose("Ana Perez", Link, Brand, Assets);

        subject.Should().Be("[Movilidad Andina] Verifica tu identidad");
        html.Should().Contain(Brand.LogoUrl!).And.Contain("alt=\"Movilidad Andina\"").And.Contain("#123ABC");
        html.Should().NotContain("flit-logo.png").And.NotContain("#557EFF");
        html.Should().Contain("Movilidad Andina</strong> necesita confirmar tu identidad");
    }

    [Fact]
    public void MarcaBlancaSinLogo_MuestraElNombreEnTexto_NuncaUnaImagenRota()
    {
        var (_, html) = ManualCaptureEmailComposer.Compose("Ana", Link, Brand with { LogoUrl = null }, Assets);

        html.Should().NotContain("<img");
        html.Should().Contain("Movilidad Andina");
    }

    [Fact]
    public void ColorMalFormado_NoSeInterpola()
    {
        var (_, html) = ManualCaptureEmailComposer.Compose("Ana", Link, Brand with { Primary = "red;} body{display:none" }, Assets);

        html.Should().NotContain("display:none").And.Contain("#162744");
    }

    [Fact]
    public void SinNombre_SaludaSinNombre()
    {
        var (_, html) = ManualCaptureEmailComposer.Compose(null, Link, EmailTheme.Flit, Assets);

        html.Should().Contain("Hola.").And.NotContain("Hola ");
    }

    [Fact]
    public void ElLayout_ParametrizaBannerTituloBotonYPasos_SinDuplicarEstructura()
    {
        var baseline = ManualCaptureEmailComposer.Compose("Ana", Link, EmailTheme.Flit, Assets).Html;
        var rechazo = new IdentityVerificationEmailContent(
            Subject: "[FLIT 2.0] Tu validación fue rechazada",
            Eyebrow: "Verificación de identidad",
            Title: "Vuelve a intentarlo",
            IntroHtml: "Motivo de ejemplo.",
            Steps: ["Paso A", "Paso B"],
            ButtonLabel: "Reintentar",
            FallbackLinkIntro: "Copia este enlace:",
            FooterNote: "Vale 24 horas.",
            BannerText: "Tu validación anterior fue rechazada.",
            BannerTone: IdentityEmailBannerTone.Warning);

        var html = IdentityVerificationEmailLayout.Render(EmailTheme.Flit, Assets, "Ana", Link, rechazo);

        WebUtility.HtmlDecode(html).Should().Contain("Tu validación anterior fue rechazada.");
        html.Should().Contain("#FFF4E0");
        html.Should().Contain("Vuelve a intentarlo").And.Contain("Reintentar").And.Contain("Paso B").And.NotContain("Paso C");
        baseline.Should().NotContain("#FFF4E0", "sin banner no se pinta la caja de aviso");
        html.Should().Contain("max-width:520px").And.Contain("border-radius:16px");
    }

    [Fact]
    public void LaPrevisualizacion_UsaElMismoComposerQueElEnvioReal()
    {
        var (subject, html) = NotificationSampleRenderer.Render("identidad.captura-manual", NotificationChannel.FlitSmtp, Assets);
        var real = ManualCaptureEmailComposer.Compose(
            ManualCaptureEmailPreviewSample.SampleRecipientName, ManualCaptureEmailPreviewSample.SampleLink, EmailTheme.Flit, Assets);

        (subject, html).Should().Be(real);
    }

    [Fact]
    public void LaPrevisualizacion_ConTemaDeMarca_ReflejaLaRed_YPorRentingSaleConTemaFlit()
    {
        var (_, flit) = NotificationSampleRenderer.Render("identidad.captura-manual", NotificationChannel.FlitSmtp, Assets, theme: Brand);
        var (_, renting) = NotificationSampleRenderer.Render("identidad.captura-manual", NotificationChannel.TenantApi, Assets, theme: Brand);

        flit.Should().Contain("Movilidad Andina");
        renting.Should().NotContain("Movilidad Andina", "el canal Renting nunca recibe tema de marca (HU #12428 AC8)");
    }
}
