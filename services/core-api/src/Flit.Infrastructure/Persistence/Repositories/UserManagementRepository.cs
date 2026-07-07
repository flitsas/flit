using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.UserManagement;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #10619: repositorio compartido de suspensión/desactivación/reactivación de usuarios,
/// consumido por <c>SuspendUserHandler</c>/<c>UnsuspendUserHandler</c> desde
/// <c>SecurityEndpoints</c> y <c>AdminOtEndpoints</c> (antes cada endpoint manipulaba
/// <c>FlitDbContext</c> directamente y duplicaba la misma lógica).
/// </summary>
public sealed class UserManagementRepository(FlitDbContext db) : IUserManagementRepository
{
    public async Task<UserManagementTarget?> FindTargetAsync(Guid userId, bool includeDeleted, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Where(u => u.Id == userId);
        if (!includeDeleted)
            query = query.Where(u => u.DeletedAt == null);

        var user = await query
            .Select(u => new { u.Id, u.HomeTenantId, u.Email, u.DisplayName, u.DeletedAt })
            .FirstOrDefaultAsync(ct);

        // Mismo criterio que UserRoleAssignmentRepository.UserBelongsToTenantAsync: el tenant del
        // usuario es su HomeTenantId. Sin él no hay tenant sobre el que aplicar la acción.
        if (user is null || user.HomeTenantId is null)
            return null;

        return new UserManagementTarget(user.Id, user.HomeTenantId.Value, user.Email, user.DisplayName, user.DeletedAt);
    }

    public async Task<IReadOnlyList<ActiveAdminRoleAssignment>> GetActiveAdminRoleAssignmentsAsync(
        Guid userId, CancellationToken ct)
    {
        return await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.UserId == userId
                  && a.DeletedAt == null
                  && r.DeletedAt == null
                  && r.IsActive
                  && AdminRoleCodes.All.Contains(r.Code)
            select new ActiveAdminRoleAssignment(r.Code, a.TenantId)
        ).Distinct().ToListAsync(ct);
    }

    public async Task<bool> HasOtherActiveAdminsAsync(
        string roleCode, Guid? scopeTenantId, Guid excludingUserId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var candidates =
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            join u in db.Users.AsNoTracking() on a.UserId equals u.Id
            where r.Code == roleCode
                  && r.IsActive
                  && r.DeletedAt == null
                  && a.DeletedAt == null
                  && u.DeletedAt == null
                  && a.UserId != excludingUserId
            select new { a.UserId, a.TenantId };

        if (scopeTenantId is not null)
            candidates = candidates.Where(x => x.TenantId == scopeTenantId.Value);

        var candidateUserIds = await candidates.Select(x => x.UserId).Distinct().ToListAsync(ct);
        if (candidateUserIds.Count == 0)
            return false;

        // Un candidato NO cuenta como "otro admin disponible" si él mismo está suspendido
        // (temporal vigente o indefinidamente) — igual criterio de "activo" usado en login.
        var suspendedUserIds = await db.UserTempSuspensions
            .AsNoTracking()
            .Where(s => candidateUserIds.Contains(s.UserId)
                        && s.DeletedAt == null
                        && s.StartsAt <= now
                        && (s.EndsAt == null || s.EndsAt >= now))
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(ct);

        return candidateUserIds.Except(suspendedUserIds).Any();
    }

    public async Task<int> CloseActiveSuspensionsAsync(
        Guid tenantId, Guid userId, DateTimeOffset now, Guid? closedBy, CancellationToken ct)
    {
        return await db.UserTempSuspensions
            .Where(s => s.UserId == userId && s.TenantId == tenantId
                        && s.DeletedAt == null && (s.EndsAt == null || s.EndsAt >= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.DeletedAt, now)
                .SetProperty(x => x.DeletedBy, closedBy),
                ct);
    }

    public async Task<Guid> CreateSuspensionAsync(
        Guid tenantId,
        Guid userId,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        string reason,
        Guid? createdBy,
        CancellationToken ct)
    {
        var suspension = new UserTempSuspension
        {
            TenantId = tenantId,
            UserId = userId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Reason = reason,
            CreatedAt = startsAt,
            CreatedBy = createdBy,
        };

        db.UserTempSuspensions.Add(suspension);
        await db.SaveChangesAsync(ct);
        return suspension.Id;
    }
}
