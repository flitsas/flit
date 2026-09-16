using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetTokenRepository(FlitDbContext db) : IPasswordResetTokenRepository
{
    public async Task CreateAsync(
        Guid userId,
        string tokenHash,
        string purpose,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            Purpose = purpose,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PasswordResetTokenRecord?> FindActiveByTokenHashAsync(
        string tokenHash,
        string purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = await db.PasswordResetTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == tokenHash
                        && t.Purpose == purpose
                        && t.UsedAt == null
                        && t.ExpiresAt > now)
            .Select(t => new { t.Id, t.UserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (token is null)
            return null;

        // HU #12423 AC5 — mismo criterio que UserAccountRepository.FindActiveByEmailAsync: el
        // tenant se deriva de la asignación de rol activa (puede no existir).
        var tenantId = await db.UserRoleAssignments
            .AsNoTracking()
            .Where(a => a.UserId == token.UserId && a.DeletedAt == null)
            .Select(a => (Guid?)a.TenantId)
            .FirstOrDefaultAsync(cancellationToken);

        return new PasswordResetTokenRecord(token.Id, token.UserId, tenantId);
    }

    public async Task MarkUsedAsync(Guid tokenId, DateTimeOffset usedAt, CancellationToken cancellationToken)
    {
        await db.PasswordResetTokens
            .Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, usedAt), cancellationToken);
    }

    public async Task InvalidateActiveForUserAsync(
        Guid userId,
        string purpose,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken)
    {
        await db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, usedAt), cancellationToken);
    }
}
