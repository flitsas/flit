using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.ActivateAccount;

public sealed class ActivateAccountHandler(
    IInvitationRepository invitationRepository,
    ISecureTokenGenerator tokenGenerator,
    IUserActivationRepository userActivationRepository,
    IPasswordHasher passwordHasher,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext)
{
    public async Task<AccountActivatedResult> HandleAsync(
        ActivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenGenerator.HashToken(command.Token);

        var invitation = await invitationRepository.FindPendingByTokenHashAsync(tokenHash, cancellationToken);
        if (invitation is null)
        {
            await AuditAsync(null, AuditVocabulary.Results.Failure, "invitation_invalid", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidInvitationTokenException();
        }

        if (!PasswordPolicy.IsCompliant(command.Password))
        {
            await AuditAsync(invitation, AuditVocabulary.Results.Failure, "weak_password", cancellationToken)
                .ConfigureAwait(false);
            throw new WeakPasswordException();
        }

        var passwordHash = passwordHasher.Hash(command.Password);
        var activatedAt = DateTimeOffset.UtcNow;

        await userActivationRepository.ActivateAsync(
            new ActivationData(
                invitation.InvitationId,
                invitation.Email,
                invitation.FullName,
                passwordHash,
                invitation.TenantId,
                invitation.RoleIds,
                invitation.InvitedBy,
                activatedAt),
            cancellationToken);

        await AuditAsync(invitation, AuditVocabulary.Results.Success, null, cancellationToken).ConfigureAwait(false);

        return new AccountActivatedResult();
    }

    // HU #10678 — sin contraseñas/token en el rastro. El "afectado" es la invitación (el usuario
    // se crea en este mismo paso; su id lo asigna la infraestructura de activación).
    private async Task AuditAsync(
        PendingInvitation? invitation, string result, string? errorCode, CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(
            new AdminAuditEntry(
                invitation?.TenantId,
                TenantType: null,
                AuditVocabulary.Modules.Authentication,
                EntityName: "invitation",
                AuditVocabulary.Operations.ActivateAccount,
                result,
                errorCode,
                ActorUserId: null,
                TargetEntityType: invitation is null ? null : "INVITATION",
                TargetEntityId: invitation?.InvitationId,
                auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);
}
