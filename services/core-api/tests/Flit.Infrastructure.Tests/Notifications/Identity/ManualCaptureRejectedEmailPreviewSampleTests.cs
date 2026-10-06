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
/// HU #13299 (Feature #13282, Épica #13202) — la muestra del correo de rechazo de captura manual en el módulo de notificaciones:
/// una sola fuente de HTML con <see cref="ManualCaptureEmailComposer.ComposeRejected"/>, sin «Kyverum», motivo escapado.
/// </summary>
public sealed class ManualCaptureRejectedEmailPreviewSampleTests
{
    private const string Assets = "https://app.flit.example/email-assets";
    private const string Id = "identidad.captura-manual-rechazo";

    private static readonly EmailTheme Brand = new(
        EmailThemeKind.Brand, "Movilidad Andina", "https://dev.flit.example/api/v1/public/branding/logos/11111111-1111-4111-8111-111111111111",
        "#123ABC", "#456DEF", "#FFFFFF", 3);

    [Fact]
    public void LaMuestra_EsIdenticaAUnComposeRejectedDirecto()
    {
        var (subject, html) = NotificationSampleRenderer.Render(Id, NotificationChannel.FlitSmtp, Assets);
        var real = ManualCaptureEmailComposer.ComposeRejected(
            ManualCaptureRejectedEmailPreviewSample.SampleRecipientName,
            ManualCaptureRejectedEmailPreviewSample.SampleReasonLabel,
            ManualCaptureRejectedEmailPreviewSample.SampleLink,
            EmailTheme.Flit,
            Assets);

        (subject, html).Should().Be(real);
        Id.Should().Be(ManualCaptureEmailComposer.RejectionTemplateId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaMuestra_NoContieneKyverum_AsuntoIncluido(bool conMarca)
    {
        var (subject, html) = NotificationSampleRenderer.Render(Id, NotificationChannel.FlitSmtp, Assets, theme: conMarca ? Brand : null);

        subject.Should().NotContainEquivalentOf("kyverum");
        html.Should().NotContainEquivalentOf("kyverum");
        WebUtility.HtmlDecode(html).Should().NotContainEquivalentOf("kyverum");
    }

    [Fact]
    public void LaMuestra_MuestraElMotivoDeLaListaCerrada_TituloBotonYAsunto()
    {
        var (subject, html) = NotificationSampleRenderer.Render(Id, NotificationChannel.FlitSmtp, Assets);
        var text = WebUtility.HtmlDecode(html);

        subject.Should().Be("[FLIT 2.0] Repite tu verificación de identidad");
        text.Should().Contain("Motivo: Imagen borrosa.").And.Contain("Repite tu verificación").And.Contain("Repetir mi verificación");
        html.Should().Contain("#FFF4E0");
        html.Should().NotMatchRegex(@"(?<![#\w])\d{6,}(?!\w)", "sin números largos tipo documento (los colores #RRGGBB no cuentan)");
    }

    [Fact]
    public void ElMotivo_SeEscapa()
    {
        var (_, html) = ManualCaptureEmailComposer.ComposeRejected(
            "Ana", "<script>alert(1)</script>", "https://app.flit.example/captura-manual/tok", EmailTheme.Flit, Assets);

        html.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public void ConTemaDeMarca_ReflejaLaRed_YPorRentingSaleConTemaFlit()
    {
        var (_, flit) = NotificationSampleRenderer.Render(Id, NotificationChannel.FlitSmtp, Assets, theme: Brand);
        var (_, renting) = NotificationSampleRenderer.Render(Id, NotificationChannel.TenantApi, Assets, theme: Brand);

        flit.Should().Contain("Movilidad Andina");
        renting.Should().NotContain("Movilidad Andina", "el canal Renting nunca recibe tema de marca (HU #12428 AC8)");
    }
}
