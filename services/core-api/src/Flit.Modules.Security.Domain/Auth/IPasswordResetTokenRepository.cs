namespace Flit.Modules.Security.Domain.Auth;

/// <summary>Proyección mínima de un token de recuperación activo.</summary>
public sealed record PasswordResetTokenRecord(Guid Id, Guid UserId);

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
