using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.ChangePassword;

/// <summary>
/// Cambio voluntario de contraseña (HU #10171, AC1/AC2, RF24). Verifica la contraseña
/// actual, valida la complejidad de la nueva y persiste el nuevo hash. Al actualizar
/// <c>PasswordChangedAt</c> se deja la marca para invalidación de sesiones previas.
/// </summary>
public sealed class ChangePasswordHandler(
    IUserAccountRepository userAccountRepository,
    IPasswordHasher passwordHasher,
    PasswordRecoveryOptions options,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext)
{
    public async Task HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsCompliant(command.NewPassword, options.MinPasswordLength))
        {
            await AuditAsync(command.UserId, AuditVocabulary.Results.Failure, "weak_password", cancellationToken)
                .ConfigureAwait(false);
            throw new WeakPasswordException();
        }

        var currentHash = await userAccountRepository.GetPasswordHashAsync(command.UserId, cancellationToken);
        if (currentHash is null)
        {
            await AuditAsync(command.UserId, AuditVocabulary.Results.Failure, "invalid_credentials", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidCredentialsException();
        }

        if (!passwordHasher.Verify(command.CurrentPassword, currentHash))
        {
            await AuditAsync(command.UserId, AuditVocabulary.Results.Failure, "invalid_current_password", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidCurrentPasswordException();
        }

        var newHash = passwordHasher.Hash(command.NewPassword);
        await userAccountRepository.UpdatePasswordHashAsync(
            command.UserId, newHash, DateTimeOffset.UtcNow, mustChangePassword: false, cancellationToken);

        await AuditAsync(command.UserId, AuditVocabulary.Results.Success, null, cancellationToken)
            .ConfigureAwait(false);
    }

    // HU #10678 — sin contraseñas en el rastro: solo desenlace + el propio usuario (actor=afectado).
    private async Task AuditAsync(
        Guid userId, string result, string? errorCode, CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                AuditVocabulary.Modules.Authentication,
                EntityName: "user",
                AuditVocabulary.Operations.ChangePassword,
                result,
                errorCode,
                ActorUserId: userId,
                TargetEntityType: "USER",
                TargetEntityId: userId,
                auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);
}
