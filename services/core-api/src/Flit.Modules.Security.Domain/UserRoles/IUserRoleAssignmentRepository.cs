namespace Flit.Modules.Security.Domain.UserRoles;

public interface IUserRoleAssignmentRepository
{
    Task<bool> UserBelongsToTenantAsync(Guid userId, Guid tenantId, CancellationToken ct);
    Task<bool> RoleIsActiveInTenantAsync(Guid roleId, Guid tenantId, CancellationToken ct);
    Task<UserRoleAssignmentSnapshot?> GetActiveAssignmentAsync(Guid userId, Guid tenantId, CancellationToken ct);
    Task SoftDeleteAssignmentAsync(Guid assignmentId, Guid deletedBy, CancellationToken ct);
    Task<Guid> CreateAssignmentAsync(AssignRoleData data, CancellationToken ct);
}

public sealed record AssignRoleData(Guid TenantId, Guid UserId, Guid RoleId, Guid AssignedBy);

public sealed record UserRoleAssignmentSnapshot(Guid Id, Guid UserId, Guid RoleId);
