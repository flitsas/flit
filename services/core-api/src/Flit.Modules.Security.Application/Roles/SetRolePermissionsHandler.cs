using Flit.Modules.Security.Domain.PermissionOverrides;
using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class SetRolePermissionsHandler(IRoleRepository repository)
{
    public async Task<RoleDetail> HandleAsync(SetRolePermissionsCommand command, CancellationToken ct)
    {
        var role = await repository.GetByIdAsync(command.RoleId, ct);
        if (role is null)
            throw new RoleNotFoundException();

        // HU #13664 AC1: «crear exige ver» por familia de trámite (detrás del flag de enforcement).
        var permissionIds = await FamilyPermissionGuard.ApplyAsync(
            repository, role.Permissions.Select(p => p.Slug), command.PermissionIds, ct);

        // HU #13644: un rol de compañía no lleva permisos OT; no tendrían efecto (la política OtModule rechaza la ruta).
        if (string.Equals(role.TargetEntityType, "COMPANY", StringComparison.Ordinal)
            && !string.Equals(role.Code, "SuperAdmin", StringComparison.OrdinalIgnoreCase) && permissionIds.Count > 0)
        {
            var infos = await repository.GetPermissionInfosAsync(permissionIds, ct);
            if (infos.Any(i => OtPermissions.IsOt(i.Slug) || OtPermissions.IsOt(i.ModuleCode)))
                throw new OtPermissionNotApplicableToRoleException();
        }

        // HU #12964 (contrato v1 §4): un rol solo tiene permisos de módulos de su producto. SuperAdmin
        // queda exento por su bypass (§2.1). La base lo exige también (tr_role_permissions_same_product);
        // validarlo aquí devuelve un error claro en vez de la excepción del disparador.
        if (!string.Equals(role.Code, "SuperAdmin", StringComparison.OrdinalIgnoreCase) && permissionIds.Count > 0)
        {
            var products = await repository.GetPermissionProductCodesAsync(permissionIds, ct);
            if (products.Any(p => !string.Equals(p, role.ProductCode, StringComparison.Ordinal)))
                throw new RolePermissionProductMismatchException();
        }

        await repository.SetPermissionsAsync(command.RoleId, permissionIds, ct);

        var updated = await repository.GetByIdAsync(command.RoleId, ct);
        return updated!;
    }
}
