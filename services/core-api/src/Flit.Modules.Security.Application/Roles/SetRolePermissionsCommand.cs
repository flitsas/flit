namespace Flit.Modules.Security.Application.Roles;

public sealed record SetRolePermissionsCommand(
    Guid RoleId,
    Guid TenantId,
    IReadOnlyList<Guid> PermissionIds);
