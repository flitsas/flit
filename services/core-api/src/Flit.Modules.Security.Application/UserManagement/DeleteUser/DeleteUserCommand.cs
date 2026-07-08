namespace Flit.Modules.Security.Application.UserManagement.DeleteUser;

/// <summary>
/// Elimina (soft-delete reversible) a un usuario dentro del alcance del caller (HU #10623).
/// <paramref name="RowVersion"/> es obligatorio — concurrencia optimista igual que
/// <c>UpdateUserCommand</c> (el valor leído por el caller de <c>TenantUserDto.RowVersion</c> /
/// <c>OtUserDto.RowVersion</c>). <paramref name="CallerIsSuperAdmin"/> — mismo criterio que
/// <c>SuspendUserCommand</c>: SuperAdmin actúa sobre el tenant REAL del usuario objetivo.
/// </summary>
public sealed record DeleteUserCommand(
    Guid CallerTenantId,
    Guid UserId,
    long RowVersion,
    Guid CallerId,
    bool CallerIsSuperAdmin);
