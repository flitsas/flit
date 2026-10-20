namespace Flit.Modules.Security.Application.Auth.AdminResetPassword;

/// <summary>Reset administrativo: el caller (admin) restablece la contraseña del usuario objetivo.</summary>
public sealed record AdminResetPasswordCommand(
    Guid? CallerTenantId,
    string CallerRoleCode,
    IReadOnlyList<string> CallerPermissions,
    string TargetEmail);
