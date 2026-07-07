using Flit.Modules.Security.Domain.Permissions;
using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class SetTenantRolePermissionsHandler(
    IRoleRepository roleRepository,
    IPermissionRepository permissionRepository)
{
    public async Task<RoleDetail> HandleAsync(SetTenantRolePermissionsCommand command, CancellationToken ct)
    {
        // HU #10505 / ADR-0023: security.roles es ahora un catálogo GLOBAL (sin tenant_id), así
        // que ya no existe una relación rol-tenant que validar aquí. command.CallerTenantId se
        // conserva en la firma (no se toca el endpoint que lo alimenta, fuera de alcance de esta
        // HU) pero deja de usarse para la verificación de propiedad del rol. El re-diseño de la
        // gobernanza de este endpoint (quién puede delegar permisos sobre un rol global) es HU
        // #10508.
        var role = await roleRepository.GetByIdAsync(command.RoleId, ct);
        if (role is null)
            throw new RoleNotFoundException();

        var callerPermissionIds = await permissionRepository.ResolveActiveSlugIdsAsync(
            command.CallerPermissionSlugs, ct);
        var callerIdSet = new HashSet<Guid>(callerPermissionIds);

        var unauthorized = command.PermissionIds.Where(id => !callerIdSet.Contains(id)).ToList();
        if (unauthorized.Count > 0)
            throw new InsufficientPermissionsForDelegationException();

        await roleRepository.SetPermissionsAsync(command.RoleId, command.PermissionIds, ct);

        var updated = await roleRepository.GetByIdAsync(command.RoleId, ct);
        return updated!;
    }
}
