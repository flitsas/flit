namespace Flit.Modules.Security.Application.UserManagement.UnsuspendUser;

/// <summary>
/// Reactiva a un usuario levantando su suspensión/desactivación activa (HU #10619).
/// <paramref name="CallerIsSuperAdmin"/> — ver <c>SuspendUserCommand</c> (fix AC3).
/// </summary>
public sealed record UnsuspendUserCommand(
    Guid CallerTenantId,
    Guid UserId,
    Guid CallerId,
    bool CallerIsSuperAdmin);
