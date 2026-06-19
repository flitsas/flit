using Flit.Modules.Security.Domain.Auth;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Flit.Infrastructure.Email;

/// <summary>Envío de correo vía SMTP usando MailKit. Implementación por defecto fuera de dev.</summary>
public sealed class SmtpEmailSender(EmailSettings settings) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.DefaultSenderName, settings.DefaultSenderEmail));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var socketOptions = settings.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.Auto;

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
    }
}
