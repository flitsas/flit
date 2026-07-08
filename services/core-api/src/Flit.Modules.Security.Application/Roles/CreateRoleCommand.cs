namespace Flit.Modules.Security.Application.Roles;

public sealed record CreateRoleCommand(
    string TargetEntityType,
    string Code,
    string Name,
    string? Description);
