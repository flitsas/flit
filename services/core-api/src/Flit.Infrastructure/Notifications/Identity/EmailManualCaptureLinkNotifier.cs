using Flit.Infrastructure.Notifications.Theme;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Application.Identity;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Notifications.Identity;

/// <summary>
/// Envía por correo el enlace de captura manual al titular (HU #13287, Épica #13202), por el MISMO canal de correo de la
/// plataforma que el resto de avisos (<see cref="IEmailSender"/>: enrutado por canal del tenant, bitácora de entregas, tema de
/// marca por red) y con la base pública del frontend de <c>PublicBranding:PublicBaseUrl</c> — la misma que usa el tema de correo
/// para el logotipo; cada ambiente la define. El token en claro vive solo en memoria durante el envío: no se registra en logs
/// ni en auditoría. Nunca lanza: devuelve <c>false</c> si el correo no salió.
/// </summary>
public sealed partial class EmailManualCaptureLinkNotifier(
    IEmailSender emailSender,
    IEmailThemeResolver themeResolver,
    EmailThemePublicBrandingOptions options,
    ILogger<EmailManualCaptureLinkNotifier> logger) : IManualCaptureLinkNotifier
{
    public async Task<bool> NotifyAsync(ManualCaptureLink link, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (string.IsNullOrWhiteSpace(link.RecipientEmail))
            return false;

        try
        {
            var theme = await themeResolver.ResolveAsync(link.TenantId, ct).ConfigureAwait(false);
            var url = ManualCaptureEmailComposer.BuildLink(options.PublicBaseUrl, link.Token);
            var (subject, html) = ManualCaptureEmailComposer.Compose(link.RecipientName, url, link.ExpiresAt, theme);

            var message = new EmailMessage(
                link.TenantId,
                ManualCaptureEmailComposer.TemplateId,
                link.RecipientEmail.Trim(),
                link.RecipientName ?? string.Empty,
                subject,
                html)
            {
                ThemeKind = theme.KindWireValue,
                ThemeVersion = theme.IsBrand ? theme.Version : null,
                SenderDisplayName = theme.IsBrand ? theme.PlatformName : null,
            };

            var result = await emailSender.SendAsync(message, ct).ConfigureAwait(false);
            if (!result.Success)
                LogSendFailed(logger, link.ValidationId, result.Outcome);
            return result.Success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin ex.Message/ex: podría arrastrar el enlace o el correo. Solo el tipo.
            LogSendError(logger, link.ValidationId, ex.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Identidad manual: el correo del enlace de captura de la validación {ValidationId} no salió ({Outcome}).")]
    private static partial void LogSendFailed(ILogger logger, Guid validationId, EmailSendOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Identidad manual: error al enviar el correo del enlace de captura de la validación {ValidationId} ({ErrorType}).")]
    private static partial void LogSendError(ILogger logger, Guid validationId, string errorType);
}
