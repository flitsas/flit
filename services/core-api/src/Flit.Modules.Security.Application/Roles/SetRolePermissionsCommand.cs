namespace Flit.Modules.Security.Application.Roles;

public sealed record SetRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds);
