namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Datos del usuario necesarios para componer el correo de recuperación.
/// </summary>
/// <param name="TenantId">
/// HU #11358 — tenant del usuario, derivado de su asignación de rol activa igual que
/// <see cref="AdminTargetUser.TenantId"/> (ver <c>UserAccountRepository</c>). Puede ser
/// <c>null</c> si el usuario no tiene ninguna asignación de rol activa; ese es el único caso en
/// que <see cref="EmailMessage.TenantId"/> viaja nulo hacia el puerto de envío.
/// </param>
public sealed record PasswordRecoveryUser(Guid UserId, string Email, string DisplayName, Guid? TenantId);

/// <summary>Usuario objetivo de una acción administrativa (incluye tenant para el control de ámbito).</summary>
public sealed record AdminTargetUser(Guid UserId, string Email, string DisplayName, Guid? TenantId);

/// <summary>
/// Acceso de escritura/consulta de cuentas para los flujos de credenciales.
/// Separado del read-model de login (<see cref="IAuthUserRepository"/>).
/// </summary>
public interface IUserAccountRepository
{
    /// <summary>Usuario activo (no eliminado, estado "active") con ese email, o null.</summary>
    Task<PasswordRecoveryUser?> FindActiveByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Usuario activo objetivo (con su tenant) para una acción administrativa, o null.</summary>
    Task<AdminTargetUser?> FindActiveTargetByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Hash de contraseña actual del usuario (para verificar el cambio voluntario), o null.</summary>
    Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Reemplaza el hash de contraseña y fija el flag de cambio forzado.</summary>
    Task UpdatePasswordHashAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset changedAt,
        bool mustChangePassword,
        CancellationToken cancellationToken);
}
