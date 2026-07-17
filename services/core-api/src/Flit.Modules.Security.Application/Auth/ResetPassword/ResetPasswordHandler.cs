using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.ResetPassword;

/// <summary>
/// Redención del token de recuperación (HU #10169): valida el token vigente y fija la
/// nueva contraseña (Argon2), marca el token como usado e invalida los demás tokens
/// activos del usuario. Cualquier fallo de token responde de forma genérica.
/// </summary>
public sealed class ResetPasswordHandler(
    IPasswordResetTokenRepository tokenRepository,
    IUserAccountRepository userAccountRepository,
    ISecureTokenGenerator tokenGenerator,
    IPasswordHasher passwordHasher,
    PasswordRecoveryOptions options,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext)
{
    private const string Purpose = "password_reset";

    public async Task HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
        {
            await AuditAsync(null, AuditVocabulary.Results.Failure, "invalid_reset_token", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidResetTokenException();
        }

        if (!PasswordPolicy.IsCompliant(command.NewPassword, options.MinPasswordLength))
        {
            await AuditAsync(null, AuditVocabulary.Results.Failure, "weak_password", cancellationToken)
                .ConfigureAwait(false);
            throw new WeakPasswordException();
        }

        var now = DateTimeOffset.UtcNow;
        var tokenHash = tokenGenerator.HashToken(command.Token);

        var record = await tokenRepository.FindActiveByTokenHashAsync(tokenHash, Purpose, now, cancellationToken);
        if (record is null)
        {
            await AuditAsync(null, AuditVocabulary.Results.Failure, "invalid_reset_token", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidResetTokenException();
        }

        var newHash = passwordHasher.Hash(command.NewPassword);
        await userAccountRepository.UpdatePasswordHashAsync(
            record.UserId, newHash, now, mustChangePassword: false, cancellationToken);
        await tokenRepository.MarkUsedAsync(record.Id, now, cancellationToken);
        await tokenRepository.InvalidateActiveForUserAsync(record.UserId, Purpose, now, cancellationToken);

        await AuditAsync(record.UserId, AuditVocabulary.Results.Success, null, cancellationToken)
            .ConfigureAwait(false);
    }

    // HU #10678 — sin token/contraseña en el rastro: solo desenlace + afectado (el mismo usuario).
    private async Task AuditAsync(
        Guid? userId, string result, string? errorCode, CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                AuditVocabulary.Modules.Authentication,
                EntityName: "user",
                AuditVocabulary.Operations.ResetPassword,
                result,
                errorCode,
                ActorUserId: userId,
                TargetEntityType: userId is null ? null : "USER",
                TargetEntityId: userId,
                auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);
}
