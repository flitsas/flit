using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Email;

/// <summary>
/// Sender de desarrollo: registra el correo (incluido el enlace) en el log en lugar de
/// enviarlo. Se usa en Development cuando no hay host SMTP configurado, igual que
/// DevelopmentAuthSeeder auto-genera llaves en dev.
/// </summary>
public sealed partial class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogDevEmail(logger, message.ToEmail, message.ToName, message.Subject, message.HtmlBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[DEV EMAIL] Para: {ToEmail} <{ToName}> | Asunto: {Subject}\n{HtmlBody}")]
    private static partial void LogDevEmail(
        ILogger logger,
        string toEmail,
        string toName,
        string subject,
        string htmlBody);
}
