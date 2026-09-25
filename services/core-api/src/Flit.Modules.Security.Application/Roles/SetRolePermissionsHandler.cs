using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class SetRolePermissionsHandler(IRoleRepository repository)
{
    public async Task<RoleDetail> HandleAsync(SetRolePermissionsCommand command, CancellationToken ct)
    {
        var role = await repository.GetByIdAsync(command.RoleId, ct);
        if (role is null)
            throw new RoleNotFoundException();

        // HU #12964 (contrato v1 §4): un rol solo tiene permisos de módulos de su producto. SuperAdmin
        // queda exento por su bypass (§2.1). La base lo exige también (tr_role_permissions_same_product);
        // validarlo aquí devuelve un error claro en vez de la excepción del disparador.
        if (!string.Equals(role.Code, "SuperAdmin", StringComparison.OrdinalIgnoreCase) && command.PermissionIds.Count > 0)
        {
            var products = await repository.GetPermissionProductCodesAsync(command.PermissionIds, ct);
            if (products.Any(p => !string.Equals(p, role.ProductCode, StringComparison.Ordinal)))
                throw new RolePermissionProductMismatchException();
        }

        await repository.SetPermissionsAsync(command.RoleId, command.PermissionIds, ct);

        var updated = await repository.GetByIdAsync(command.RoleId, ct);
        return updated!;
    }
}
