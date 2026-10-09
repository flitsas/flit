using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Identity;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Net;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Identity;

/// <summary>
/// HU #13287 (Feature #13280 A5, Épica #13202) — <see cref="EmailManualCaptureLinkNotifier"/> y
/// <see cref="ManualCaptureEmailComposer"/>: el correo sale por <see cref="IEmailSender"/> (proveedor simulado) al titular, con el
/// enlace <c>{base pública}/verificacion/{token}</c>, texto corto en español, sin número de documento; nunca lanza y reporta
/// <c>false</c> cuando no sale.
/// <para>Uso: <c>await notifier.NotifyAsync(new ManualCaptureLink(id, tenant, token, vence, correo, nombre))</c>.</para>
/// </summary>
public sealed class EmailManualCaptureLinkNotifierTests
{
    private static readonly Guid Tenant = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly DateTimeOffset Vence = new(2026, 10, 7, 14, 30, 0, TimeSpan.Zero); // 09:30 hora Colombia
    private const string Token = "enlace-de-prueba-uno";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IEmailThemeResolver _themes = Substitute.For<IEmailThemeResolver>();

    public EmailManualCaptureLinkNotifierTests()
    {
        _themes.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(EmailTheme.Flit);
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);
    }

    private EmailManualCaptureLinkNotifier Notifier(string baseUrl = "https://app.flit.example/") =>
        new(_sender, _themes, new EmailThemePublicBrandingOptions { PublicBaseUrl = baseUrl },
            Options.Create(new NotificationEmailAssetsOptions { BaseUrl = "https://app.flit.example/email-assets" }),
            NullLogger<EmailManualCaptureLinkNotifier>.Instance);

    private static ManualCaptureLink Link(string? email = "titular@example.test", string? name = "Ana Perez") =>
        new(Guid.NewGuid(), Tenant, Token, Vence, email, name);

    [Fact]
    public async Task EnviaUnCorreo_AlTitular_ConElEnlaceDeCapturaManual()
    {
        EmailMessage? enviado = null;
        _sender.SendAsync(Arg.Do<EmailMessage>(m => enviado = m), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);

        var ok = await Notifier().NotifyAsync(Link(), Ct);

        ok.Should().BeTrue();
        await _sender.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        enviado!.ToEmail.Should().Be("titular@example.test");
        enviado.ToName.Should().Be("Ana Perez");
        enviado.TenantId.Should().Be(Tenant);
        enviado.TemplateKey.Should().Be("identidad.captura-manual");
        enviado.HtmlBody.Should().Contain("https://app.flit.example/verificacion/enlace-de-prueba-uno");
        enviado.Subject.Should().Be("[FLIT 2.0] Verifica tu identidad");
    }

    [Fact]
    public async Task ElCorreo_TieneElDisenoDeVerificacion_ConPasos_Vigencia_YSinDatosSensibles()
    {
        EmailMessage? enviado = null;
        _sender.SendAsync(Arg.Do<EmailMessage>(m => enviado = m), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);

        await Notifier().NotifyAsync(Link(), Ct);

        var html = WebUtility.HtmlDecode(enviado!.HtmlBody);
        html.Should().Contain("Hola Ana Perez.")
            .And.Contain("Verifica tu identidad")
            .And.Contain("Verificación de identidad")
            .And.Contain("FLIT 2.0</strong> necesita confirmar tu identidad para continuar y es quien guarda tus datos.")
            .And.Contain("Solicitado por")
            .And.Contain("PASOS DE LA VERIFICACIÓN")
            .And.Contain("Valida tus datos").And.Contain("Realiza la validación biométrica")
            .And.Contain("Captura los datos del documento").And.Contain("Firma")
            .And.Contain("Verificar mi identidad").And.Contain("#557EFF")
            .And.Contain("Si el botón no funciona, copia este enlace:")
            .And.Contain("El enlace es personal, de un solo uso y caduca en 24 horas. Ábrelo en tu celular.")
            .And.Contain("https://app.flit.example/email-assets/flit-logo.png");
        html.Should().NotContainEquivalentOf("kyverum");
        enviado.Subject.Should().NotContainEquivalentOf("kyverum");
    }

    [Fact]
    public async Task ElNombreSeEscapa_YNoSeCuelaMarcado()
    {
        EmailMessage? enviado = null;
        _sender.SendAsync(Arg.Do<EmailMessage>(m => enviado = m), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);

        await Notifier().NotifyAsync(Link(name: "<script>alert(1)</script>"), Ct);

        enviado!.HtmlBody.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public async Task SinCorreoDelTitular_NoEnvia_YDevuelveFalse()
    {
        var ok = await Notifier().NotifyAsync(Link(email: " "), Ct);

        ok.Should().BeFalse();
        await _sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SiElProveedorRechaza_DevuelveFalse_SinLanzar()
    {
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable));

        var ok = await Notifier().NotifyAsync(Link(), Ct);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task SiElTransporteLanza_DevuelveFalse_SinLanzar()
    {
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task<EmailSendResult>>(_ => throw new InvalidOperationException("boom"));

        var ok = await Notifier().NotifyAsync(Link(), Ct);

        ok.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://app.flit.example", "https://app.flit.example/verificacion/t1")]
    [InlineData("https://app.flit.example///", "https://app.flit.example/verificacion/t1")]
    [InlineData("", "http://localhost:3000/verificacion/t1")]
    public void BuildLink_NormalizaLaBase(string baseUrl, string esperado) =>
        ManualCaptureEmailComposer.BuildLink(baseUrl, "t1").Should().Be(esperado);

    [Fact]
    public void BuildLink_CodificaElTokenParaLaRuta() =>
        ManualCaptureEmailComposer.BuildLink("https://x.test", "a/b c").Should().Be("https://x.test/verificacion/a%2Fb%20c");
}
