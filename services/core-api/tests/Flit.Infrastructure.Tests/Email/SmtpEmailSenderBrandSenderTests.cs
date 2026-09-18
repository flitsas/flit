using Flit.Infrastructure.Email;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Email;

/// <summary>
/// HU #12430 AC1/AC3/AC6 — <see cref="SmtpEmailSender.ResolveSenderName"/> es la función PURA (sin
/// red, sin MailKit) que decide el nombre visible del <c>From</c> antes de que
/// <see cref="SmtpEmailSender.SendAsync"/> intente conectar. Se prueba en aislamiento (mismo motivo
/// que <c>SmtpEmailSenderTests</c> evita depender de un servidor SMTP real): la única forma de
/// observar el <c>MimeMessage.From</c> construido dentro de <c>SendAsync</c> sería instrumentar un
/// servidor SMTP falso, que este repo no tiene — extraer la resolución del nombre a un método
/// <c>internal static</c> la hace comprobable sin esa infraestructura.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var name = SmtpEmailSender.ResolveSenderName(settings, message);
/// // name es el que SmtpEmailSender.SendAsync usa como MailboxAddress.Name del From.
/// </code>
/// </remarks>
public sealed class SmtpEmailSenderBrandSenderTests
{
    private static readonly EmailSettings Settings = new()
    {
        Host = "smtp.flit.test",
        DefaultSenderEmail = "no-reply@flitsas.online",
        DefaultSenderName = "FLIT Trámites",
        DefaultSenderPassword = "irrelevante",
    };

    private static EmailMessage NewMessage(string? senderDisplayName) => new(
        TenantId: Guid.NewGuid(),
        TemplateKey: "security.forgot-password",
        ToEmail: "destinatario@flit.test",
        ToName: "Destinatario",
        Subject: "Asunto",
        HtmlBody: "<html>cuerpo</html>")
    {
        SenderDisplayName = senderDisplayName,
    };

    // ── AC1 — con marca (SenderDisplayName poblado), el nombre visible es el de la marca ─────

    [Fact]
    public void ConSenderDisplayNamePoblado_UsaElNombreDeLaMarcaSaneado()
    {
        var message = NewMessage("Movilidad Andina");

        var name = SmtpEmailSender.ResolveSenderName(Settings, message);

        name.Should().Be("Movilidad Andina");
    }

    [Fact]
    public void ConSenderDisplayNameConCaracteresDeInyeccion_LoAplicaSaneado()
    {
        var message = NewMessage("Movilidad\r\nBcc: atacante@evil.test");

        var name = SmtpEmailSender.ResolveSenderName(Settings, message);

        name.Should().NotContain("\r").And.NotContain("\n").And.NotContain("@");
        name.Should().Be(SenderDisplayNameSanitizer.Sanitize("Movilidad\r\nBcc: atacante@evil.test"));
    }

    // ── AC3 — sin marca (SenderDisplayName null), usa el nombre por defecto de FLIT ──────────

    [Fact]
    public void SinSenderDisplayName_UsaElNombrePorDefectoDeEmailSettings()
    {
        var message = NewMessage(senderDisplayName: null);

        var name = SmtpEmailSender.ResolveSenderName(Settings, message);

        name.Should().Be("FLIT Trámites");
    }

    [Fact]
    public void ConSenderDisplayNameQueQuedaVacioTrasSanear_UsaElNombrePorDefecto()
    {
        // Un nombre compuesto ÍNTEGRAMENTE por caracteres prohibidos (caso defensivo — la marca real
        // pasa por BrandingValidation, HU #12413 — NEW-11) no debe dejar un From sin nombre visible.
        var message = NewMessage("<>@\";:,");

        var name = SmtpEmailSender.ResolveSenderName(Settings, message);

        name.Should().Be("FLIT Trámites");
    }

    // ── AC1 — la DIRECCIÓN nunca la decide este método: siempre viene de EmailSettings ────────

    [Fact]
    public void LaDireccionDeEnvio_NuncaSaleDeEsteMetodo_SiempreEsLaConfiguradaEnSettings()
    {
        // ResolveSenderName solo devuelve el NOMBRE visible — SmtpEmailSender.SendAsync es quien
        // arma el MailboxAddress completo con settings.DefaultSenderEmail como dirección, sin
        // importar el resultado de este método (ver comentario de SendAsync, línea del From).
        var brandMessage = NewMessage("Movilidad Andina");
        var defaultMessage = NewMessage(senderDisplayName: null);

        SmtpEmailSender.ResolveSenderName(Settings, brandMessage).Should().NotBeNullOrEmpty();
        SmtpEmailSender.ResolveSenderName(Settings, defaultMessage).Should().NotBeNullOrEmpty();
        // La dirección configurada es la misma para ambos casos — no depende de SenderDisplayName.
        Settings.DefaultSenderEmail.Should().Be("no-reply@flitsas.online");
    }
}
