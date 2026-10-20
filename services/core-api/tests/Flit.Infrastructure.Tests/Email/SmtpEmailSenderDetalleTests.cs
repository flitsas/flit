using Flit.Infrastructure.Email;
using FluentAssertions;
using MailKit.Net.Smtp;
using Xunit;

namespace Flit.Infrastructure.Tests.Email;

/// <summary>
/// HU #13359 — <see cref="SmtpEmailSender.DetalleSmtp"/>: lo que el SuperAdmin ve en mensajes muertos sobre la respuesta
/// del servidor. Códigos y una lectura en español; nunca el texto crudo del servidor.
/// </summary>
public sealed class SmtpEmailSenderDetalleTests
{
    [Theory]
    [InlineData(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed,
        "5.2.2 STOREDRV.Submission.Exception:SendAsDeniedException; Mailbox full", "SMTP 554 5.2.2 · buzón lleno")]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable,
        "5.1.1 User unknown", "SMTP 550 5.1.1 · el destinatario no existe")]
    [InlineData(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed,
        "5.9.99 algo raro", "SMTP 554 5.9.99")]
    [InlineData(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.ServiceNotAvailable,
        "Service not available", "SMTP 421")]
    public void DaCodigoYLectura(SmtpErrorCode error, SmtpStatusCode status, string respuesta, string esperado) =>
        SmtpEmailSender.DetalleSmtp(new SmtpCommandException(error, status, respuesta)).Should().Be(esperado);

    [Fact]
    public void NoCopiaElTextoDelServidor()
    {
        var detalle = SmtpEmailSender.DetalleSmtp(new SmtpCommandException(
            SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable, "5.7.1 smtp.servidor.interno 10.0.0.4 usuario@flit"));

        detalle.Should().Be("SMTP 550 5.7.1 · rechazado por política del servidor");
    }
}
