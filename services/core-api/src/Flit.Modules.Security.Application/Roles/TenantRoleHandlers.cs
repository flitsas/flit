using System.Text.RegularExpressions;
using Flit.Modules.Security.Application.Products;
using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

/// <summary>Roles que ve el tenant: los globales COMPANY (solo lectura) y los propios (HU #13441 AC5).</summary>
public sealed class ListTenantRolesHandler(IRoleRepository repository)
{
    public Task<IReadOnlyList<RoleSummary>> HandleAsync(Guid tenantId, string targetEntityType, CancellationToken ct) =>
        repository.ListVisibleToTenantAsync(tenantId, targetEntityType, ct);
}

/// <summary>Detalle de un rol visible para el tenant; otro tenant o inexistente = no encontrado.</summary>
public sealed class GetTenantRoleHandler(IRoleRepository repository)
{
    public async Task<RoleDetail> HandleAsync(Guid tenantId, Guid roleId, CancellationToken ct) =>
        await repository.GetVisibleToTenantAsync(tenantId, roleId, ct) ?? throw new RoleNotFoundException();
}

/// <summary>Permisos que el caller puede ofrecer en el selector de un rol propio (AC2 de la HU #13443).</summary>
public sealed class ListGrantablePermissionsHandler(IRoleRepository repository)
{
    public async Task<IReadOnlyList<PermissionInfo>> HandleAsync(
        Guid tenantId,
        IReadOnlyCollection<string> callerPermissions,
        CancellationToken ct)
    {
        var enabled = await repository.GetEnabledProductCodesAsync(tenantId, ct);
        var all = await repository.ListGrantablePermissionsAsync([.. enabled], ct);
        var held = callerPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. all.Where(p => !PrivilegeCeiling.IsPlatformOnly(p.Slug) && held.Contains(p.Slug))];
    }
}

/// <summary>Crea un rol COMPANY propio del tenant con sus permisos, respetando el tope de privilegios.</summary>
public sealed partial class CreateTenantRoleHandler(IRoleRepository repository)
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{1,49}$")]
    private static partial Regex CodePattern();

    public async Task<Guid> HandleAsync(CreateTenantRoleCommand command, CancellationToken ct)
    {
        // AC4: el Admin de Compañía solo crea roles COMPANY.
        if (!string.Equals(command.TargetEntityType, "COMPANY", StringComparison.Ordinal))
            throw new InvalidTargetEntityTypeException();

        var code = command.Code?.Trim() ?? string.Empty;
        var name = command.Name?.Trim() ?? string.Empty;
        if (!CodePattern().IsMatch(code) || name.Length is 0 or > 100)
            throw new InvalidRoleInputException();

        if (!ProductCodes.All.Contains(command.ProductCode, StringComparer.Ordinal))
            throw new InvalidRoleProductException();

        var enabled = await repository.GetEnabledProductCodesAsync(command.TenantId, ct);
        if (!enabled.Contains(command.ProductCode))
            throw new PrivilegeCeilingException(PrivilegeCeilingCodes.ModuleNotEnabled, [command.ProductCode]);

        var permissionIds = command.PermissionIds.Distinct().ToList();
        await EnforceAsync(repository, command.ProductCode, permissionIds, command.CallerPermissions, enabled, ct);

        if (await repository.CodeTakenForTenantAsync(command.TenantId, code, ct))
            throw new RoleCodeDuplicateException();

        var id = await repository.CreateAsync(
            new CreateRoleData("COMPANY", code, name, command.Description?.Trim(), command.ProductCode, command.TenantId),
            ct);
        if (permissionIds.Count > 0)
            await repository.SetPermissionsAsync(id, permissionIds, ct);
        return id;
    }

    internal static async Task EnforceAsync(
        IRoleRepository repository,
        string roleProductCode,
        IReadOnlyList<Guid> permissionIds,
        IReadOnlyCollection<string> callerPermissions,
        IReadOnlySet<string> enabled,
        CancellationToken ct)
    {
        if (permissionIds.Count == 0)
            return;

        var infos = await repository.GetPermissionInfosAsync(permissionIds, ct);
        PrivilegeCeiling.Enforce(permissionIds, infos, callerPermissions, enabled);

        // Mismo invariante que SetRolePermissionsHandler (disparador de BD): un rol solo lleva permisos de su producto.
        if (infos.Any(i => !string.Equals(i.ProductCode, roleProductCode, StringComparison.Ordinal)))
            throw new RolePermissionProductMismatchException();
    }
}

/// <summary>Reemplaza los permisos de un rol propio del tenant (los globales y los de otro tenant no se tocan).</summary>
public sealed class SetTenantRolePermissionsHandler(IRoleRepository repository)
{
    public async Task<RoleDetail> HandleAsync(SetTenantRolePermissionsCommand command, CancellationToken ct)
    {
        var role = await OwnedRoleAsync(repository, command.TenantId, command.RoleId, ct);
        var permissionIds = command.PermissionIds.Distinct().ToList();

        var enabled = await repository.GetEnabledProductCodesAsync(command.TenantId, ct);
        await CreateTenantRoleHandler.EnforceAsync(repository, role.ProductCode, permissionIds, command.CallerPermissions, enabled, ct);

        await repository.SetPermissionsAsync(command.RoleId, permissionIds, ct);
        return (await repository.GetByIdAsync(command.RoleId, ct))!;
    }

    internal static async Task<RoleDetail> OwnedRoleAsync(IRoleRepository repository, Guid tenantId, Guid roleId, CancellationToken ct)
    {
        var role = await repository.GetVisibleToTenantAsync(tenantId, roleId, ct) ?? throw new RoleNotFoundException();
        // Global (TenantId null) o de otro tenant: solo lectura para el Admin de Compañía (AC5).
        if (role.TenantId != tenantId)
            throw new RoleNotOwnedException();
        return role;
    }
}

/// <summary>Cambia nombre y descripción de un rol propio del tenant.</summary>
public sealed class UpdateTenantRoleHandler(IRoleRepository repository)
{
    public async Task<RoleDetail> HandleAsync(UpdateTenantRoleCommand command, CancellationToken ct)
    {
        await SetTenantRolePermissionsHandler.OwnedRoleAsync(repository, command.TenantId, command.RoleId, ct);
        var name = command.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
            throw new InvalidRoleInputException();

        await repository.UpdateDetailsAsync(command.RoleId, name, command.Description?.Trim(), ct);
        return (await repository.GetByIdAsync(command.RoleId, ct))!;
    }
}

/// <summary>Elimina un rol propio; 409 si tiene usuarios asignados (AC2).</summary>
public sealed class DeleteTenantRoleHandler(IRoleRepository repository)
{
    public async Task HandleAsync(Guid tenantId, Guid roleId, CancellationToken ct)
    {
        await SetTenantRolePermissionsHandler.OwnedRoleAsync(repository, tenantId, roleId, ct);
        if (await repository.HasActiveUsersAsync(roleId, ct))
            throw new RoleHasActiveUsersException();
        await repository.SoftDeleteAsync(roleId, ct);
    }
}
