namespace Flit.Modules.Security.Application.UserRoles;

public sealed record AssignRoleCommand(Guid TenantId, Guid UserId, Guid RoleId, Guid AssignedBy);
