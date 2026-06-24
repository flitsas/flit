using Flit.Modules.Security.Domain.UserRoles;

namespace Flit.Modules.Security.Application.UserRoles;

public sealed class AssignRoleHandler(IUserRoleAssignmentRepository repo)
{
    public async Task HandleAsync(AssignRoleCommand cmd, CancellationToken ct)
    {
        if (cmd.UserId == cmd.AssignedBy)
            throw new SelfRoleAssignmentException();

        // AC3 — verify user belongs to caller's tenant via HomeTenantId
        if (!await repo.UserBelongsToTenantAsync(cmd.UserId, cmd.TenantId, ct))
            throw new UserOutOfScopeException();

        // AC4 — verify role exists and is active in the tenant
        if (!await repo.RoleIsActiveInTenantAsync(cmd.RoleId, cmd.TenantId, ct))
            throw new RoleForAssignmentNotFoundException();

        // AC2 — soft-delete existing active assignment if present
        var existing = await repo.GetActiveAssignmentAsync(cmd.UserId, cmd.TenantId, ct);
        if (existing is not null)
            await repo.SoftDeleteAssignmentAsync(existing.Id, cmd.AssignedBy, ct);

        // AC1 / AC2 — create new assignment
        await repo.CreateAssignmentAsync(
            new AssignRoleData(cmd.TenantId, cmd.UserId, cmd.RoleId, cmd.AssignedBy),
            ct);
    }
}
