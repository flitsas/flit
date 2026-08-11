using System.Net.Sockets;
using Flit.Modules.Security.Domain.Auth;
using MailKit.Net.Smtp;
using MailKit.Security;
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
/// </summary>
public sealed class SmtpEmailSender(EmailSettings settings) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Host)
            || string.IsNullOrWhiteSpace(settings.DefaultSenderEmail)
            || (!settings.DisableAuthentication && string.IsNullOrWhiteSpace(settings.DefaultSenderPassword)))
        {
            return EmailSendResult.Failed(EmailSendOutcome.ConfigurationIncomplete);
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.DefaultSenderName, settings.DefaultSenderEmail));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var socketOptions = settings.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.Auto;

        try
        {
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
        catch (AuthenticationException)
        {
            return EmailSendResult.Failed(EmailSendOutcome.AuthenticationFailed);
        }
        catch (SmtpCommandException ex)
        {
            return EmailSendResult.Failed(MapCommandException(ex));
        }
        catch (SmtpProtocolException)
        {
            return EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
        }
        catch (SocketException ex)
        {
            return EmailSendResult.Failed(
                ex.SocketErrorCode == SocketError.TimedOut
                    ? EmailSendOutcome.TimedOut
                    : EmailSendOutcome.ProviderUnavailable);
        }
        catch (TimeoutException)
        {
            return EmailSendResult.Failed(EmailSendOutcome.TimedOut);
        }
        catch (IOException)
        {
            return EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Red de seguridad: cualquier excepción de MailKit/TLS no enumerada explícitamente
            // arriba (p. ej. SslHandshakeException por un certificado que no valida, que MailKit
            // no deriva de AuthenticationException) cae aquí en vez de propagarse — el contrato
            // (AC2/AC3) es que este puerto NUNCA lanza por un fallo de transporte. La excepción
            // real, con su mensaje técnico, solo queda en la traza de logs de la infraestructura
            // (fuera de este método); el resultado que ve el llamador es siempre el genérico.
            return EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
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
}
