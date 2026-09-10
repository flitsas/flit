using System.Net.Sockets;
using Flit.Modules.Security.Domain.Auth;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Flit.Infrastructure.Email;

/// <summary>
/// Envío de correo vía SMTP usando MailKit. Implementación por defecto fuera de dev.
/// HU #11358 AC1 — no lee <see cref="EmailMessage.TenantId"/> del contexto de request (no hay
/// HttpContext aquí ni se consulta ninguno): el tenant llega ya resuelto en el mensaje. Este
/// adaptador todavía no lo usa (reservado para el enrutamiento por canal de la HU #11362 y la
/// bitácora de la HU #11363); su sola presencia en el contrato es lo que exige el AC1.
/// AC2/AC3/AC4 — nunca lanza por un fallo de transporte: cada excepción conocida de MailKit se
/// mapea a una causa del catálogo cerrado <see cref="EmailSendOutcome"/> y el mensaje de vuelta
/// es siempre el texto genérico de <see cref="EmailSendResult.Failed"/> (nunca host, puerto,
/// credencial ni el texto crudo del proveedor).
/// La excepción original SÍ se registra vía <see cref="ILogger"/> (nivel Error, con el
/// <see cref="EmailSendOutcome"/> mapeado, host, puerto y remitente configurado — nunca la
/// contraseña) en cada rama <c>catch</c>, incluido el retorno temprano por configuración
/// incompleta: sin esto, un fallo de envío no dejaba ningún rastro diagnosticable en el servidor.
/// </summary>
public sealed partial class SmtpEmailSender(EmailSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Host)
            || string.IsNullOrWhiteSpace(settings.DefaultSenderEmail)
            || (!settings.DisableAuthentication && string.IsNullOrWhiteSpace(settings.DefaultSenderPassword)))
        {
            // No se loguea settings.DefaultSenderPassword ni fragmento alguno — host/puerto/remitente
            // sí, porque son datos de despliegue (no secretos) y son lo que hace falta para diagnosticar.
            LogConfigurationIncomplete(logger, settings.Host, settings.Port, settings.DefaultSenderEmail);
            return EmailSendResult.Failed(EmailSendOutcome.ConfigurationIncomplete);
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.DefaultSenderName, settings.DefaultSenderEmail));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
        foreach (var bcc in message.BccEmails)
        {
            if (!string.IsNullOrWhiteSpace(bcc))
                mime.Bcc.Add(MailboxAddress.Parse(bcc.Trim()));
        }
        mime.Subject = message.Subject;
        var bodyBuilder = new BodyBuilder { HtmlBody = message.HtmlBody };
        foreach (var attachment in message.Attachments)
            bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content);
        mime.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        var socketOptions = settings.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.Auto;

        try
        {
            client.CheckCertificateRevocation = settings.CheckCertificateRevocation;
            await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);

            if (!settings.DisableAuthentication)
            {
                await client.AuthenticateAsync(
                    settings.DefaultSenderEmail,
                    settings.DefaultSenderPassword,
                    cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            return EmailSendResult.Sent;
        }
        catch (AuthenticationException ex)
        {
            const EmailSendOutcome outcome = EmailSendOutcome.AuthenticationFailed;
            LogSendFailed(logger, ex, outcome, settings.Host, settings.Port, settings.DefaultSenderEmail);
            return EmailSendResult.Failed(outcome);
        }
        catch (SmtpCommandException ex)
        {
            var outcome = MapCommandException(ex);
            LogSendFailed(logger, ex, outcome, settings.Host, settings.Port, settings.DefaultSenderEmail);
            return EmailSendResult.Failed(outcome);
        }
        catch (SmtpProtocolException ex)
        {
            const EmailSendOutcome outcome = EmailSendOutcome.ProviderUnavailable;
            LogSendFailed(logger, ex, outcome, settings.Host, settings.Port, settings.DefaultSenderEmail);
            return EmailSendResult.Failed(outcome);
        }
        catch (SocketException ex)
        {
            var outcome = ex.SocketErrorCode == SocketError.TimedOut
                ? EmailSendOutcome.TimedOut
                : EmailSendOutcome.ProviderUnavailable;
            LogSendFailed(logger, ex, outcome, settings.Host, settings.Port, settings.DefaultSenderEmail);
            return EmailSendResult.Failed(outcome);
        }
        catch (TimeoutException ex)
        {
            const EmailSendOutcome outcome = EmailSendOutcome.TimedOut;
            LogSendFailed(logger, ex, outcome, settings.Host, settings.Port, settings.DefaultSenderEmail);
            return EmailSendResult.Failed(outcome);
        }
        catch (IOException ex)
        {
            const EmailSendOutcome outcome = EmailSendOutcome.ProviderUnavailable;
            LogSendFailed(logger, ex, outcome, settings.Host, settings.Port, settings.DefaultSenderEmail);
            return EmailSendResult.Failed(outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Red de seguridad: cualquier excepción de MailKit/TLS no enumerada explícitamente
            // arriba (p. ej. SslHandshakeException por un certificado que no valida, que MailKit
            // no deriva de AuthenticationException) cae aquí en vez de propagarse — el contrato
            // (AC2/AC3) es que este puerto NUNCA lanza por un fallo de transporte. La excepción
            // real, con su mensaje técnico y su stack trace, SÍ queda registrada (LogSendFailed,
            // primer argumento) en la traza de logs de esta clase; el EmailSendResult que ve el
            // llamador sigue siendo siempre el genérico (AC4), nunca el texto técnico crudo.
            const EmailSendOutcome outcome = EmailSendOutcome.ProviderUnavailable;
            LogSendFailed(logger, ex, outcome, settings.Host, settings.Port, settings.DefaultSenderEmail);
            return EmailSendResult.Failed(outcome);
        }
    }

    /// <summary>
    /// <see cref="SmtpErrorCode"/> distingue destinatario de contenido con precisión; para el
    /// resto de comandos rechazados el <see cref="SmtpStatusCode"/> numérico decide entre
    /// límite de tasa y proveedor no disponible (heurística documentada, no una tabla oficial
    /// de MailKit).
    /// </summary>
    private static EmailSendOutcome MapCommandException(SmtpCommandException ex) => ex.ErrorCode switch
    {
        SmtpErrorCode.RecipientNotAccepted => EmailSendOutcome.RecipientRejected,
        SmtpErrorCode.MessageNotAccepted => EmailSendOutcome.ContentRejected,
        _ => MapByStatusCode(ex.StatusCode),
    };

    private static EmailSendOutcome MapByStatusCode(SmtpStatusCode statusCode) => statusCode switch
    {
        SmtpStatusCode.InsufficientStorage => EmailSendOutcome.RateLimited,
        SmtpStatusCode.MailboxBusy => EmailSendOutcome.RateLimited,
        SmtpStatusCode.TransactionFailed => EmailSendOutcome.ContentRejected,
        SmtpStatusCode.ExceededStorageAllocation => EmailSendOutcome.ContentRejected,
        _ => EmailSendOutcome.ProviderUnavailable,
    };

    // NUNCA agregar settings.DefaultSenderPassword (ni un fragmento) a ninguno de estos mensajes.
    // Host/puerto/remitente sí: son datos de despliegue, no secretos, y son lo que hace falta para
    // diagnosticar un fallo de transporte SMTP. El destinatario (message.ToEmail) tampoco se loguea
    // aquí — es dato personal (Ley 1581) y esta clase ni siquiera lo necesita en las ramas de fallo.

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "SMTP: configuración incompleta, no se intentó el envío (host={Host}, puerto={Port}, "
            + "remitente={SenderEmail}).")]
    private static partial void LogConfigurationIncomplete(ILogger logger, string host, int port, string senderEmail);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "SMTP: fallo al enviar el correo, causa={Outcome} (host={Host}, puerto={Port}, "
            + "remitente={SenderEmail}).")]
    private static partial void LogSendFailed(
        ILogger logger, Exception exception, EmailSendOutcome outcome, string host, int port, string senderEmail);
}
