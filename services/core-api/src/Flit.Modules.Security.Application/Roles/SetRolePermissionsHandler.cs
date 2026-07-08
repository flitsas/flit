using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class SetRolePermissionsHandler(IRoleRepository repository)
{
    public async Task<RoleDetail> HandleAsync(SetRolePermissionsCommand command, CancellationToken ct)
    {
        var role = await repository.GetByIdAsync(command.RoleId, ct);
        if (role is null)
            throw new RoleNotFoundException();

        await repository.SetPermissionsAsync(command.RoleId, command.PermissionIds, ct);

        var updated = await repository.GetByIdAsync(command.RoleId, ct);
        return updated!;
    }
}
