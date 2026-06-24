namespace Flit.Modules.Security.Application.Roles;

public sealed record CreateRoleCommand(
    Guid TenantId,
    string Code,
    string Name,
    string? Description);
