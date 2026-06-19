using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class UserAccountRepository(FlitDbContext db) : IUserAccountRepository
{
    public async Task<PasswordRecoveryUser?> FindActiveByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        return await db.Users
            .AsNoTracking()
            .Where(u => u.DeletedAt == null
                        && u.Status == "active"
                        && EF.Functions.ILike(u.Email, normalizedEmail))
            .Select(u => new PasswordRecoveryUser(u.Id, u.Email, u.DisplayName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AdminTargetUser?> FindActiveTargetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.DeletedAt == null
                        && u.Status == "active"
                        && EF.Functions.ILike(u.Email, normalizedEmail))
            .Select(u => new { u.Id, u.Email, u.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
            return null;

        // El tenant del usuario se deriva de su asignación de rol (puede no existir).
        var tenantId = await db.UserRoleAssignments
            .AsNoTracking()
            .Where(a => a.UserId == user.Id && a.DeletedAt == null)
            .Select(a => (Guid?)a.TenantId)
            .FirstOrDefaultAsync(cancellationToken);

        return new AdminTargetUser(user.Id, user.Email, user.DisplayName, tenantId);
    }

    public async Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await db.UserCredentials
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.PasswordHash)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpdatePasswordHashAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset changedAt,
        bool mustChangePassword,
        CancellationToken cancellationToken)
    {
        await db.UserCredentials
            .Where(c => c.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(c => c.PasswordHash, passwordHash)
                    .SetProperty(c => c.PasswordChangedAt, changedAt)
                    .SetProperty(c => c.MustChangePassword, mustChangePassword)
                    .SetProperty(c => c.UpdatedAt, changedAt),
                cancellationToken);
    }
}
