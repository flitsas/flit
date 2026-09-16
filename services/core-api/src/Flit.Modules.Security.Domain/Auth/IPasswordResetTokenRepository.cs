namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Proyección mínima de un token de recuperación activo.
/// </summary>
/// <param name="TenantId">
/// HU #12423 AC5 — tenant del usuario dueño del token, derivado de su asignación de rol activa
/// (mismo criterio que <c>PasswordRecoveryUser.TenantId</c>). Puede ser <c>null</c> si el usuario
/// no tiene ninguna asignación de rol activa; en ese caso la coherencia de dominio se resuelve
/// como "sin red" (comportamiento de hoy).
/// </param>
public sealed record PasswordResetTokenRecord(Guid Id, Guid UserId, Guid? TenantId = null);

/// <summary>Acceso a la tabla <c>security.password_reset_tokens</c>.</summary>
public interface IPasswordResetTokenRepository
{
    Task CreateAsync(
        Guid userId,
        string tokenHash,
        string purpose,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    /// <summary>Token vigente (no usado y no expirado) que coincide con el hash y el propósito.</summary>
    Task<PasswordResetTokenRecord?> FindActiveByTokenHashAsync(
        string tokenHash,
        string purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task MarkUsedAsync(Guid tokenId, DateTimeOffset usedAt, CancellationToken cancellationToken);

    /// <summary>Invalida (marca como usados) los demás tokens activos del usuario para ese propósito.</summary>
    Task InvalidateActiveForUserAsync(
        Guid userId,
        string purpose,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken);
}
