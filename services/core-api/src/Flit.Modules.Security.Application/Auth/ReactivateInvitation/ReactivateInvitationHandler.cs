using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Security.Application.Auth.ReactivateInvitation;

/// <summary>
/// HU #11552 / ADR-0048: reactiva una invitación cancelada. Es UNA sola acción — no un
/// "des-borrado" seguido de un reenvío — que vuelve la invitación a <c>pending</c>, regenera el
/// token (SIEMPRE nuevo, el enlace anterior queda muerto) y reenvía el correo. No es idempotente
/// a propósito: una segunda llamada sobre la misma invitación ya "pending" lanza
/// <see cref="InvitationNotCancelledException"/>.
///
/// Orden de validación (contrato de la HU): alcance (404) → está "cancelled" (409) → correo
/// libre (409, tres sub-chequeos) → roles vigentes (409) → cooldown anti-abuso (429, compartido
/// con <see cref="ResendInvitation.ResendInvitationHandler"/> para que cancel→reactivate en bucle
/// no sea un bypass).
/// </summary>
public sealed partial class ReactivateInvitationHandler(
    IInvitationRepository invitationRepository,
    IUserManagementRepository userManagementRepository,
    ISecureTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    InvitationOptions options,
    ILogger<ReactivateInvitationHandler> logger)
{
    public async Task<ReactivateInvitationResult> HandleAsync(
        ReactivateInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var invitation = await invitationRepository.FindForReactivateAsync(
            command.InvitationId, command.ScopeTenantId, cancellationToken);

        // Mismo error para "no existe" y "fuera de alcance" — no revela la existencia de
        // invitaciones de otros tenants (mapeado a 404 en el endpoint).
        if (invitation is null)
            throw new InvitationNotFoundException();

        if (invitation.Status != "cancelled")
            throw new InvitationNotCancelledException();

        // ── Correo libre ──────────────────────────────────────────────────────────────────
        // Mismo trío de chequeos que CreateInvitationHandler, en el mismo orden: cuenta
        // soft-deleted, otra invitación pendiente, usuario activo.
        var existingByEmail = await userManagementRepository.FindByEmailIncludingDeletedAsync(
            invitation.Email, cancellationToken);
        if (existingByEmail is { IsDeleted: true })
            throw new UserEmailBelongsToDeletedAccountException();

        var hasPending = await invitationRepository.ExistsPendingAsync(
            invitation.TenantId, invitation.Email, cancellationToken);
        if (hasPending)
            throw new InvitationAlreadyPendingException();

        var userExists = await invitationRepository.UserExistsWithEmailAsync(
            invitation.Email, cancellationToken);
        if (userExists)
            throw new UserAlreadyExistsException();

        // ── Roles vigentes ────────────────────────────────────────────────────────────────
        foreach (var roleId in invitation.RoleIds)
        {
            var roleExists = await invitationRepository.RoleExistsInTenantAsync(
                invitation.TenantId, roleId, cancellationToken);
            if (!roleExists)
                throw new RoleNotFoundException();
        }

        // ── Cooldown anti-abuso ───────────────────────────────────────────────────────────
        var now = DateTimeOffset.UtcNow;
        if (invitation.LastSentAt is { } lastSentAt)
        {
            var elapsed = now - lastSentAt;
            if (elapsed < options.ResendCooldown)
                throw new ResendCooldownActiveException(options.ResendCooldown - elapsed);
        }

        var token = tokenGenerator.Generate();

        // Red de la condición de carrera: el repositorio traduce el 23505 de
        // uq_user_invitations_tenant_email_pending a InvitationAlreadyPendingException si otra
        // invitación se volvió "pending" entre la validación de arriba y este UPDATE.
        await invitationRepository.ReactivateAsync(
            invitation.InvitationId, token.TokenHash, now, command.ReactivatedBy, cancellationToken);

        var link = InvitationEmailTemplate.BuildActivateLink(options.ActivateUrlBase, token.RawToken);
        var composed = InvitationEmailTemplate.Compose(invitation.FullName, link);
        var message = new EmailMessage(
            invitation.TenantId, "security.invitation", invitation.Email, invitation.Email, composed.Subject, composed.HtmlBody);

        LogActivationLinkDev(logger, link);

        var sendResult = await emailSender.SendAsync(message, cancellationToken);
        if (!sendResult.Success)
            LogEmailFailed(logger, invitation.InvitationId, sendResult.Outcome);

        return new ReactivateInvitationResult(invitation.InvitationId, invitation.Email, sendResult.Success);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[retryable] Reactivation email failed for invitation {InvitationId}. Invitation is pending again. Cause: {Outcome}.")]
    private static partial void LogEmailFailed(ILogger logger, Guid invitationId, EmailSendOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[DEV] Reactivated activation link (use this to test locally): {Link}")]
    private static partial void LogActivationLinkDev(ILogger logger, string link);
}
