using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

/// <summary>Activa/desactiva un rol del catálogo global (HU #10505). Gobernanza SuperAdmin (HU #10508).</summary>
public sealed class SetRoleActiveHandler(IRoleRepository repository)
{
    public async Task HandleAsync(Guid roleId, bool isActive, CancellationToken ct)
    {
        var role = await repository.GetByIdAsync(roleId, ct);
        if (role is null)
            throw new RoleNotFoundException();

        await repository.SetActiveAsync(roleId, isActive, ct);
    }
}
