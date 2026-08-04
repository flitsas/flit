namespace Flit.Modules.Security.Application.UserRoles;

/// <param name="TenantId">Tenant del caller. Se ignora si <paramref name="CallerIsSuperAdmin"/>
/// es <c>true</c>: el SuperAdmin administra usuarios de cualquier tenant y el suyo es el interno
/// de FLIT, así que el alcance lo da el usuario destino.</param>
public sealed record AssignRoleCommand(
    Guid TenantId, Guid UserId, Guid RoleId, Guid AssignedBy, bool CallerIsSuperAdmin = false);
