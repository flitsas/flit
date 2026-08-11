using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Security.Application.Auth.ResendInvitation;

/// <summary>
/// HU #10625: reenvía una invitación pendiente. SIEMPRE regenera el token de activación (solo
/// se persiste su hash — reutilizar el token anterior es técnicamente imposible), invalidando
/// el enlace previo (AC1). Rechaza el reenvío si la invitación no está pendiente (AC3,
/// <see cref="InvitationNotPendingException"/>) o si no ha pasado el cooldown anti-abuso desde
/// el último envío (AC2, <see cref="ResendCooldownActiveException"/>).
/// </summary>
public sealed partial class ResendInvitationHandler(
    IInvitationRepository invitationRepository,
    ISecureTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    InvitationOptions options,
    ILogger<ResendInvitationHandler> logger)
{
    public async Task<ResendInvitationResult> HandleAsync(
        ResendInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var invitation = await invitationRepository.FindForResendAsync(
            command.InvitationId, command.ScopeTenantId, cancellationToken);

        if (invitation is null)
            throw new InvitationNotFoundException();

        if (invitation.Status != "pending")
            throw new InvitationNotPendingException();

        var now = DateTimeOffset.UtcNow;
        if (invitation.LastSentAt is { } lastSentAt)
        {
            var elapsed = now - lastSentAt;
            if (elapsed < options.ResendCooldown)
                throw new ResendCooldownActiveException(options.ResendCooldown - elapsed);
        }

        var token = tokenGenerator.Generate();

        await invitationRepository.UpdateResendAsync(
            invitation.InvitationId, token.TokenHash, now, command.ResentBy, cancellationToken);

        var link = InvitationEmailTemplate.BuildActivateLink(options.ActivateUrlBase, token.RawToken);
        var composed = InvitationEmailTemplate.Compose(invitation.FullName, link);
        var message = new EmailMessage(invitation.TenantId, invitation.Email, invitation.Email, composed.Subject, composed.HtmlBody);

        LogActivationLinkDev(logger, link);

        // HU #11358 AC2/AC3 — el puerto ya no lanza por un fallo de transporte: el resultado
        // tipado reemplaza el try/catch.
        var sendResult = await emailSender.SendAsync(message, cancellationToken);
        if (!sendResult.Success)
            LogEmailFailed(logger, invitation.InvitationId, sendResult.Outcome);

        return new ResendInvitationResult(invitation.InvitationId, invitation.Email, sendResult.Success);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[retryable] Resend activation email failed for invitation {InvitationId}. Invitation remains pending. Cause: {Outcome}.")]
    private static partial void LogEmailFailed(ILogger logger, Guid invitationId, EmailSendOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[DEV] Resent activation link (use this to test locally): {Link}")]
    private static partial void LogActivationLinkDev(ILogger logger, string link);
}
