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
    PasswordRecoveryOptions options)
{
    private const string Purpose = "password_reset";

    public async Task HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            throw new InvalidResetTokenException();

        if (string.IsNullOrWhiteSpace(command.NewPassword)
            || command.NewPassword.Length < options.MinPasswordLength)
            throw new WeakPasswordException();

        var now = DateTimeOffset.UtcNow;
        var tokenHash = tokenGenerator.HashToken(command.Token);

        var record = await tokenRepository.FindActiveByTokenHashAsync(tokenHash, Purpose, now, cancellationToken);
        if (record is null)
            throw new InvalidResetTokenException();

        var newHash = passwordHasher.Hash(command.NewPassword);
        await userAccountRepository.UpdatePasswordHashAsync(
            record.UserId, newHash, now, mustChangePassword: false, cancellationToken);
        await tokenRepository.MarkUsedAsync(record.Id, now, cancellationToken);
        await tokenRepository.InvalidateActiveForUserAsync(record.UserId, Purpose, now, cancellationToken);
    }
}
