using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Identity;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Identity;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Identity;

/// <summary>
/// HU #13299 (Feature #13282 C4, Épica #13202) — variante «rechazo» del correo de captura manual: el mismo notificador y canal
/// (<see cref="IEmailSender"/> simulado) envía el motivo en texto legible (etiqueta de la lista cerrada) y el enlace NUEVO, con su
/// propia plantilla, sin datos sensibles y con el motivo escapado.
/// <para>Uso: <c>await notifier.NotifyAsync(new ManualCaptureLink(id, tenant, token, vence, correo, nombre, "Imagen borrosa"))</c>.</para>
/// </summary>
public sealed class EmailManualCaptureLinkNotifierRechazoTests
{
    private static readonly Guid Tenant = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateTimeOffset Vence = new(2026, 10, 7, 14, 30, 0, TimeSpan.Zero); // 09:30 hora Colombia
    private const string Token = "enlace-de-prueba-dos";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IEmailThemeResolver _themes = Substitute.For<IEmailThemeResolver>();

    public EmailManualCaptureLinkNotifierRechazoTests()
    {
        _themes.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(EmailTheme.Flit);
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);
    }

    private EmailManualCaptureLinkNotifier Notifier() =>
        new(_sender, _themes, new EmailThemePublicBrandingOptions { PublicBaseUrl = "https://app.flit.example/" },
            Options.Create(new NotificationEmailAssetsOptions { BaseUrl = "https://app.flit.example/email-assets" }),
            NullLogger<EmailManualCaptureLinkNotifier>.Instance);

    // El layout codifica el texto (WebUtility.HtmlEncode): las tildes salen como entidades numéricas.
    private static string Html(string text) => WebUtility.HtmlEncode(text);

    private static ManualCaptureLink Link(string? label) =>
        new(Guid.NewGuid(), Tenant, Token, Vence, "titular@example.test", "Ana Perez", label);

    [Fact]
    public async Task AC4_ConMotivo_EnviaLaVarianteRechazo_ConElMotivoLegible_YElEnlaceNuevo()
    {
        EmailMessage? enviado = null;
        _sender.SendAsync(Arg.Do<EmailMessage>(m => enviado = m), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);
        var etiqueta = ManualRejectionReasons.LabelFor(ManualRejectionReasons.RostroNoCoincide)!;

        var ok = await Notifier().NotifyAsync(Link(etiqueta), Ct);

        ok.Should().BeTrue();
        await _sender.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        enviado!.TemplateKey.Should().Be("identidad.captura-manual-rechazo").And.Be(ManualCaptureEmailComposer.RejectionTemplateId);
        enviado.ToEmail.Should().Be("titular@example.test");
        enviado.Subject.Should().Contain("Repite tu verificación de identidad");
        enviado.HtmlBody.Should().Contain(Html("Motivo: " + etiqueta))
            .And.Contain("https://app.flit.example/verificacion/enlace-de-prueba-dos")
            .And.Contain("24 horas").And.Contain("Hola Ana Perez.")
            .And.Contain(Html("Repetir mi verificación")).And.Contain(Html("Verificación de identidad"));
        enviado.HtmlBody.Should().NotContain("rostro_no_coincide", "el cliente ve el texto, no el código");
        enviado.HtmlBody.Should().NotContainEquivalentOf("kyverum", "el correo nunca nombra al proveedor");
        enviado.Subject.Should().NotContainEquivalentOf("kyverum");
    }

    [Fact]
    public async Task SinMotivo_SigueSaliendoElCorreoDeActivacion_DeSiempre()
    {
        EmailMessage? enviado = null;
        _sender.SendAsync(Arg.Do<EmailMessage>(m => enviado = m), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);

        await Notifier().NotifyAsync(Link(null), Ct);

        enviado!.TemplateKey.Should().Be("identidad.captura-manual");
        enviado.HtmlBody.Should().NotContain("Motivo:");
    }

    [Fact]
    public async Task ElMotivoSeEscapa_YNoSeCuelaMarcado()
    {
        EmailMessage? enviado = null;
        _sender.SendAsync(Arg.Do<EmailMessage>(m => enviado = m), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);

        await Notifier().NotifyAsync(Link("<b>x</b> & y"), Ct);

        enviado!.HtmlBody.Should().NotContain("<b>x</b>").And.Contain("&lt;b&gt;x&lt;/b&gt; &amp; y");
    }

    [Fact]
    public async Task SiElProveedorRechaza_DevuelveFalse_SinLanzar()
    {
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable));

        var ok = await Notifier().NotifyAsync(Link("Imagen borrosa"), Ct);

        ok.Should().BeFalse();
    }
}
