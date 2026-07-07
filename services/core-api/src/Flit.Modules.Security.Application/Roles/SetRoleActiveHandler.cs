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

        // Fix post-review #10504: solo se bloquea la DESACTIVACIÓN de roles de sistema
        // (SuperAdmin/AdminCompany/ot_admin) — desactivarlos dejaría al tenant sin admin
        // funcional. Activar (isActive=true) siempre es inofensivo y sigue permitido.
        if (!isActive && role.IsSystem)
            throw new RoleSystemLockedException();

        await repository.SetActiveAsync(roleId, isActive, ct);
    }
}
