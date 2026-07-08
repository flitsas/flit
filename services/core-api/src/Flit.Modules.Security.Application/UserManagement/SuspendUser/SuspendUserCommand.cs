namespace Flit.Modules.Security.Application.UserManagement.SuspendUser;

/// <summary>
/// Suspende o desactiva a un usuario (HU #10619). <paramref name="EndsAt"/> nulo = desactivación
/// indefinida (AC1); con valor = suspensión temporal que se reactiva sola al vencer (AC2, sin
/// cambios de comportamiento). <paramref name="CallerIsSuperAdmin"/> permite que la acción se
/// aplique sobre el tenant REAL del usuario objetivo en vez de forzar el tenant del caller (fix
/// AC3: antes el endpoint siempre usaba <c>callerTenantId</c>, rompiendo el caso SuperAdmin).
/// </summary>
public sealed record SuspendUserCommand(
    Guid CallerTenantId,
    Guid UserId,
    string Reason,
    DateTimeOffset? EndsAt,
    Guid CallerId,
    bool CallerIsSuperAdmin);
