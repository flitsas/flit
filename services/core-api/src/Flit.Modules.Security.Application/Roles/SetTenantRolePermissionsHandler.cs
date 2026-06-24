using Flit.Modules.Security.Domain.Permissions;
using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class SetTenantRolePermissionsHandler(
    IRoleRepository roleRepository,
    IPermissionRepository permissionRepository)
{
    public async Task<RoleDetail> HandleAsync(SetTenantRolePermissionsCommand command, CancellationToken ct)
    {
        var role = await roleRepository.GetByIdAsync(command.RoleId, ct);
        if (role is null || role.TenantId != command.CallerTenantId)
            throw new RoleNotFoundException();

        var callerPermissionIds = await permissionRepository.ResolveActiveSlugIdsAsync(
            command.CallerPermissionSlugs, ct);
        var callerIdSet = new HashSet<Guid>(callerPermissionIds);

        var unauthorized = command.PermissionIds.Where(id => !callerIdSet.Contains(id)).ToList();
        if (unauthorized.Count > 0)
            throw new InsufficientPermissionsForDelegationException();

        await roleRepository.SetPermissionsAsync(
            command.RoleId, command.CallerTenantId, command.PermissionIds, ct);

        var updated = await roleRepository.GetByIdAsync(command.RoleId, ct);
        return updated!;
    }
}
