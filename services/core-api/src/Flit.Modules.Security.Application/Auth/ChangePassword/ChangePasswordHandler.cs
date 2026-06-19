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
    PasswordRecoveryOptions options)
{
    public async Task HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsCompliant(command.NewPassword, options.MinPasswordLength))
            throw new WeakPasswordException();

        var currentHash = await userAccountRepository.GetPasswordHashAsync(command.UserId, cancellationToken);
        if (currentHash is null)
            throw new InvalidCredentialsException();

        if (!passwordHasher.Verify(command.CurrentPassword, currentHash))
            throw new InvalidCurrentPasswordException();

        var newHash = passwordHasher.Hash(command.NewPassword);
        await userAccountRepository.UpdatePasswordHashAsync(
            command.UserId, newHash, DateTimeOffset.UtcNow, mustChangePassword: false, cancellationToken);
    }
}
