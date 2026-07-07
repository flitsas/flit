using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del repositorio compartido de gestión administrativa del ciclo de
/// vida de un usuario: edición de perfil (HU #10621) y suspensión/desactivación/reactivación
/// (HU #10619), consumido por <c>UpdateUserHandler</c>/<c>SuspendUserHandler</c>/
/// <c>UnsuspendUserHandler</c> desde <c>SecurityEndpoints</c> y <c>AdminOtEndpoints</c> (antes
/// cada endpoint manipulaba <c>FlitDbContext</c> directamente y duplicaba la misma lógica).
/// </summary>
public sealed class UserManagementRepository(FlitDbContext db) : IUserManagementRepository
{
    public async Task<UserManagementTarget?> FindTargetAsync(Guid userId, bool includeDeleted, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Where(u => u.Id == userId);
        if (!includeDeleted)
            query = query.Where(u => u.DeletedAt == null);

        var user = await query
            .Select(u => new { u.Id, u.HomeTenantId, u.Email, u.DisplayName, u.DeletedAt, u.RowVersion })
            .FirstOrDefaultAsync(ct);

        // Mismo criterio que UserRoleAssignmentRepository.UserBelongsToTenantAsync: el tenant del
        // usuario es su HomeTenantId. Sin él no hay tenant sobre el que aplicar la acción.
        if (user is null || user.HomeTenantId is null)
            return null;

        return new UserManagementTarget(
            user.Id, user.HomeTenantId.Value, user.Email, user.DisplayName, user.DeletedAt, user.RowVersion);
    }

    public async Task<ExistingUserByEmail?> FindByEmailIncludingDeletedAsync(string email, CancellationToken ct)
    {
        // uq_users_email es un índice único GLOBAL (no parcial por deleted_at): un correo
        // soft-deleted sigue "ocupado" en BD, por eso esta búsqueda NO filtra por DeletedAt
        // (a diferencia de AuthUserRepository.FindByEmailAsync, que sí lo hace para el login).
        return await db.Users
            .AsNoTracking()
            .Where(u => EF.Functions.ILike(u.Email, email))
            .Select(u => new ExistingUserByEmail(u.Id, u.DeletedAt != null))
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? email,
        long expectedRowVersion,
        DateTimeOffset updatedAt,
        Guid? updatedBy,
        CancellationToken ct)
    {
        var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity is null)
            throw new TargetUserNotFoundException();

        // Concurrencia optimista contra la versión que el CALLER leyó (no la que acabamos de
        // traer de BD): al forzar el OriginalValue del concurrency token a expectedRowVersion,
        // el UPDATE que emite EF incluye "WHERE ... AND row_version = expectedRowVersion". Si
        // otro admin ya guardó cambios entre el fetch del formulario y este submit, row_version
        // actual en BD ya no coincide con expectedRowVersion → 0 filas afectadas → EF lanza
        // DbUpdateConcurrencyException (AC4). El trigger tr_users_row_version (BEFORE UPDATE)
        // incrementa row_version en BD de forma independiente de lo que EF intente escribir.
        db.Entry(entity).Property(e => e.RowVersion).OriginalValue = expectedRowVersion;

        if (displayName is not null)
            entity.DisplayName = displayName;
        if (email is not null)
            entity.Email = email;

        entity.UpdatedAt = updatedAt;
        entity.UpdatedBy = updatedBy;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new UserProfileConcurrencyException();
        }
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
