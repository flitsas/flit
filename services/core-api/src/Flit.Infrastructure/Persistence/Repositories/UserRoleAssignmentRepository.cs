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

    public async Task<RoleForAssignmentSnapshot?> GetActiveRoleAsync(Guid roleId, CancellationToken ct)
    {
        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id).
        return await db.Roles
            .AsNoTracking()
            .Where(r => r.Id == roleId && r.IsActive && r.DeletedAt == null)
            .Select(r => new RoleForAssignmentSnapshot(r.Id, r.TargetEntityType))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string> GetTenantTargetEntityTypeAsync(Guid tenantId, CancellationToken ct)
    {
        // Mismo criterio ya usado en SecurityEndpoints/AdminOtEndpoints: un tenant con
        // TransitOfficeProfile asociado es TRANSIT_OFFICE; el resto, COMPANY.
        var isOtTenant = await db.TransitOfficeProfiles
            .AsNoTracking()
            .AnyAsync(p => p.TenantId == tenantId, ct);
        return isOtTenant ? "TRANSIT_OFFICE" : "COMPANY";
    }

    public async Task<UserRoleAssignmentSnapshot?> GetActiveAssignmentAsync(
        Guid userId, Guid tenantId, Guid roleId, CancellationToken ct)
    {
        var entity = await db.UserRoleAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.UserId == userId && a.TenantId == tenantId && a.RoleId == roleId && a.DeletedAt == null,
                ct);

        return entity is null
            ? null
            : new UserRoleAssignmentSnapshot(entity.Id, entity.UserId, entity.RoleId);
    }

    public async Task<IReadOnlyList<UserRoleAssignmentSnapshot>> GetActiveAssignmentsAsync(
        Guid userId, Guid tenantId, CancellationToken ct)
    {
        return await db.UserRoleAssignments
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.TenantId == tenantId && a.DeletedAt == null)
            .Select(a => new UserRoleAssignmentSnapshot(a.Id, a.UserId, a.RoleId))
            .ToListAsync(ct);
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
