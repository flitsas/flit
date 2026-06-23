using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class DeleteRoleHandler(IRoleRepository repository)
{
    public async Task HandleAsync(Guid roleId, CancellationToken ct)
    {
        var role = await repository.GetByIdAsync(roleId, ct);
        if (role is null)
            throw new RoleNotFoundException();

        if (role.IsSystem)
            throw new RoleSystemLockedException();

        var hasActiveUsers = await repository.HasActiveUsersAsync(roleId, ct);
        if (hasActiveUsers)
            throw new RoleHasActiveUsersException();

        await repository.SoftDeleteAsync(roleId, ct);
    }
}
