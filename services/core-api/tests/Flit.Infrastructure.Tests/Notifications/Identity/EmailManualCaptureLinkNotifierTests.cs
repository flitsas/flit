using Flit.Infrastructure.Notifications.Identity;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Identity;

/// <summary>
/// HU #13287 (Feature #13280 A5, Épica #13202) — <see cref="EmailManualCaptureLinkNotifier"/> y
/// <see cref="ManualCaptureEmailComposer"/>: el correo sale por <see cref="IEmailSender"/> (proveedor simulado) al titular, con el
/// enlace <c>{base pública}/captura-manual/{token}</c>, texto corto en español, sin número de documento; nunca lanza y reporta
/// <c>false</c> cuando no sale.
/// <para>Uso: <c>await notifier.NotifyAsync(new ManualCaptureLink(id, tenant, token, vence, correo, nombre))</c>.</para>
/// </summary>
public sealed class EmailManualCaptureLinkNotifierTests
{
    private static readonly Guid Tenant = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly DateTimeOffset Vence = new(2026, 10, 7, 14, 30, 0, TimeSpan.Zero); // 09:30 hora Colombia
    private const string Token = "tok_ABC-123_xyz";
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
        enviado.HtmlBody.Should().Contain("https://app.flit.example/captura-manual/tok_ABC-123_xyz");
        enviado.Subject.Should().Contain("Verifica tu identidad");
    }

    [Fact]
    public async Task ElCorreo_DiceQueSePide_LaVigencia_YAQuienEscribir_SinDatosSensibles()
    {
        EmailMessage? enviado = null;
        _sender.SendAsync(Arg.Do<EmailMessage>(m => enviado = m), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);

        await Notifier().NotifyAsync(Link(), Ct);

        var html = enviado!.HtmlBody;
        html.Should().Contain("Hola Ana Perez,")
            .And.Contain("rostro").And.Contain("anverso y reverso").And.Contain("firma")
            .And.Contain("24 horas").And.Contain("07/10/2026 09:30")
            .And.Contain(ManualCaptureEmailComposer.FlitSupportEmail);
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
    [InlineData("https://app.flit.example", "https://app.flit.example/captura-manual/t1")]
    [InlineData("https://app.flit.example///", "https://app.flit.example/captura-manual/t1")]
    [InlineData("", "http://localhost:3000/captura-manual/t1")]
    public void BuildLink_NormalizaLaBase(string baseUrl, string esperado) =>
        ManualCaptureEmailComposer.BuildLink(baseUrl, "t1").Should().Be(esperado);

    [Fact]
    public void BuildLink_CodificaElTokenParaLaRuta() =>
        ManualCaptureEmailComposer.BuildLink("https://x.test", "a/b c").Should().Be("https://x.test/captura-manual/a%2Fb%20c");
}
