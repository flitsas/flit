namespace Flit.Modules.Security.Application.Roles;

/// <summary>
/// Alta de un rol del catálogo global (Panel SA). HU #13653: <paramref name="Code"/> es opcional; vacío o ausente, el
/// servidor lo genera a partir del nombre (<see cref="RoleCodeGenerator"/>). Si llega, se respeta como antes.
/// </summary>
public sealed record CreateRoleCommand(
    string TargetEntityType,
    string? Code,
    string Name,
    string? Description,
    string ProductCode = "tramites");
