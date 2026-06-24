namespace Flit.Modules.Security.Application.Roles;

public sealed record SetTenantRolePermissionsCommand(
    Guid RoleId,
    Guid CallerTenantId,
    IReadOnlyList<string> CallerPermissionSlugs,
    IReadOnlyList<Guid> PermissionIds);
