using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.UserRoles;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class UserRoleAssignmentRepository(FlitDbContext db) : IUserRoleAssignmentRepository
{
    public async Task<bool> UserBelongsToTenantAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        return await db.Users
            .AnyAsync(u => u.Id == userId && u.HomeTenantId == tenantId && u.DeletedAt == null, ct);
    }

    public async Task<bool> RoleIsActiveInTenantAsync(Guid roleId, Guid tenantId, CancellationToken ct)
    {
        // HU #10505 / ADR-0023: security.roles es ahora un catálogo GLOBAL (sin tenant_id),
        // así que "existe y está activo" ya no compara contra el tenant del Role. tenantId se
        // conserva en la firma porque sigue siendo relevante para la ASIGNACIÓN
        // (UserRoleAssignment.TenantId, resuelta en AssignRoleHandler — fuera de alcance de
        // esta HU, ver HU #10506).
        _ = tenantId;
        return await db.Roles
            .AnyAsync(r => r.Id == roleId && r.IsActive && r.DeletedAt == null, ct);
    }

    public async Task<UserRoleAssignmentSnapshot?> GetActiveAssignmentAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var entity = await db.UserRoleAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.TenantId == tenantId && a.DeletedAt == null, ct);

        return entity is null
            ? null
            : new UserRoleAssignmentSnapshot(entity.Id, entity.UserId, entity.RoleId);
    }

    public async Task SoftDeleteAssignmentAsync(Guid assignmentId, Guid deletedBy, CancellationToken ct)
    {
        await db.UserRoleAssignments
            .Where(a => a.Id == assignmentId && a.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.DeletedAt, DateTimeOffset.UtcNow)
                .SetProperty(a => a.DeletedBy, deletedBy),
                ct);
    }

    public async Task<Guid> CreateAssignmentAsync(AssignRoleData data, CancellationToken ct)
    {
        var entity = new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = data.TenantId,
            UserId = data.UserId,
            RoleId = data.RoleId,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedBy = data.AssignedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = data.AssignedBy,
            RowVersion = 0,
        };

        db.UserRoleAssignments.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }
}
