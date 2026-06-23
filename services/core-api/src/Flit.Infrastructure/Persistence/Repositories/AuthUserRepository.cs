using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class AuthUserRepository(FlitDbContext db) : IAuthUserRepository
{
    public async Task<UserAuthSnapshot?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.DeletedAt == null && EF.Functions.ILike(u.Email, normalizedEmail))
            .Select(u => new { u.Id, u.Email, u.Status, u.HomeTenantId })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
            return null;

        var credential = await db.UserCredentials
            .AsNoTracking()
            .Where(c => c.UserId == user.Id)
            .Select(c => new { c.PasswordHash, c.MustChangePassword })
            .FirstOrDefaultAsync(cancellationToken);

        if (credential is null)
            return null;

        var assignment = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.UserId == user.Id && a.DeletedAt == null
            select new { a.TenantId, a.RoleId, RoleCode = r.Code }
        ).FirstOrDefaultAsync(cancellationToken);

        // Users without a role assignment can still log in if they have a home tenant
        var tenantId = assignment?.TenantId ?? user.HomeTenantId;
        if (tenantId is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var isSuspended = await db.UserTempSuspensions
            .AsNoTracking()
            .AnyAsync(
                s => s.UserId == user.Id
                     && s.TenantId == tenantId
                     && s.DeletedAt == null
                     && s.StartsAt <= now
                     && s.EndsAt >= now,
                cancellationToken);

        var permissionSlugs = new List<string>();
        if (assignment is not null)
        {
            permissionSlugs = await (
                from rp in db.RoleGrants.AsNoTracking()
                join p in db.RbacActions.AsNoTracking() on rp.PermissionId equals p.Id
                where rp.RoleId == assignment.RoleId && p.IsActive
                select p.Slug
            ).ToListAsync(cancellationToken);
        }

        return new UserAuthSnapshot
        {
            UserId = user.Id,
            Email = user.Email,
            Status = user.Status,
            PasswordHash = credential.PasswordHash,
            MustChangePassword = credential.MustChangePassword,
            TenantId = tenantId.Value,
            RoleId = assignment?.RoleId ?? Guid.Empty,
            RoleCode = assignment?.RoleCode ?? string.Empty,
            PermissionSlugs = permissionSlugs,
            IsTemporarilySuspended = isSuspended,
        };
    }
}
