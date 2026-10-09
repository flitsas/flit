namespace Flit.Modules.Security.Application.Roles;

/// <summary>
/// HU #13441 — alta de un rol propio del tenant por su Admin de Compañía.
/// <paramref name="CallerPermissions"/> son los slugs del JWT del caller: el techo de lo que puede otorgar.
/// </summary>
public sealed record CreateTenantRoleCommand(
    Guid TenantId,
    string TargetEntityType,
    string Code,
    string Name,
    string? Description,
    string ProductCode,
    IReadOnlyList<Guid> PermissionIds,
    IReadOnlyCollection<string> CallerPermissions);

/// <summary>HU #13441 — cambio de permisos de un rol propio del tenant.</summary>
public sealed record SetTenantRolePermissionsCommand(
    Guid TenantId,
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds,
    IReadOnlyCollection<string> CallerPermissions);

/// <summary>HU #13441 — cambio de nombre y descripción de un rol propio del tenant.</summary>
public sealed record UpdateTenantRoleCommand(Guid TenantId, Guid RoleId, string Name, string? Description);
