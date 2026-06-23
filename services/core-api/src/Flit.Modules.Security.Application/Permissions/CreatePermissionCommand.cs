namespace Flit.Modules.Security.Application.Permissions;

public sealed record CreatePermissionCommand(
    Guid ModuleId,
    string Slug,
    string Name,
    string Action,
    string HttpMethod,
    string RoutePattern,
    string? Description);
