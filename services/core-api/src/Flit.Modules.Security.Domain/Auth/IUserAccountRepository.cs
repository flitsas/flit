namespace Flit.Modules.Security.Domain.Auth;

/// <summary>Datos del usuario necesarios para componer el correo de recuperación.</summary>
public sealed record PasswordRecoveryUser(Guid UserId, string Email, string DisplayName);

/// <summary>
/// Acceso de escritura/consulta de cuentas para el flujo de recuperación de contraseña.
/// Separado del read-model de login (<see cref="IAuthUserRepository"/>).
/// </summary>
public interface IUserAccountRepository
{
    /// <summary>Usuario activo (no eliminado, estado "active") con ese email, o null.</summary>
    Task<PasswordRecoveryUser?> FindActiveByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Reemplaza el hash de contraseña y limpia el flag de cambio forzado.</summary>
    Task UpdatePasswordHashAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);
}
