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

    public async Task UpdatePasswordHashAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        await db.UserCredentials
            .Where(c => c.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(c => c.PasswordHash, passwordHash)
                    .SetProperty(c => c.PasswordChangedAt, changedAt)
                    .SetProperty(c => c.MustChangePassword, false)
                    .SetProperty(c => c.UpdatedAt, changedAt),
                cancellationToken);
    }
}
