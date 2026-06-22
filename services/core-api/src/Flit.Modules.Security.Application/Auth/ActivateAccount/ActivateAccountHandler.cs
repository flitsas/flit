using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.ActivateAccount;

public sealed class ActivateAccountHandler(
    IInvitationRepository invitationRepository,
    ISecureTokenGenerator tokenGenerator,
    IUserActivationRepository userActivationRepository,
    IPasswordHasher passwordHasher)
{
    public async Task<AccountActivatedResult> HandleAsync(
        ActivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenGenerator.HashToken(command.Token);

        var invitation = await invitationRepository.FindPendingByTokenHashAsync(tokenHash, cancellationToken);
        if (invitation is null)
            throw new InvalidInvitationTokenException();

        if (!PasswordPolicy.IsCompliant(command.Password))
            throw new WeakPasswordException();

        var passwordHash = passwordHasher.Hash(command.Password);
        var activatedAt = DateTimeOffset.UtcNow;

        await userActivationRepository.ActivateAsync(
            new ActivationData(
                invitation.InvitationId,
                invitation.Email,
                passwordHash,
                invitation.TenantId,
                invitation.RoleId,
                invitation.InvitedBy,
                activatedAt),
            cancellationToken);

        return new AccountActivatedResult();
    }
}
