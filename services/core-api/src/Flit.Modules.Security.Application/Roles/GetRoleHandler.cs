using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class GetRoleHandler(IRoleRepository repository)
{
    public async Task<RoleDetail> HandleAsync(Guid roleId, CancellationToken ct)
    {
        var role = await repository.GetByIdAsync(roleId, ct);
        if (role is null)
            throw new RoleNotFoundException();

        return role;
    }
}
