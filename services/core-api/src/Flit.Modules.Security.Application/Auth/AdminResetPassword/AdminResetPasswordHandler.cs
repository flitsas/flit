using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.AdminResetPassword;

/// <summary>
/// Reset administrativo de contraseña (HU #10170, AC1, RF21-RF23). Un administrador con
/// ámbito sobre el usuario genera una contraseña temporal, actualiza el hash, marca
/// <c>must_change_password</c> y notifica por correo al usuario.
///
/// Ámbito: Superadmin (rol o permiso global) puede sobre cualquier tenant; un admin de
/// compañía requiere el permiso de reset y que el usuario pertenezca a su mismo tenant.
/// </summary>
public sealed class AdminResetPasswordHandler(
    IUserAccountRepository userAccountRepository,
    ITemporaryPasswordGenerator temporaryPasswordGenerator,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender)
{
    /// <summary>Permiso requerido para resetear contraseñas dentro del propio tenant.</summary>
    public const string ResetPermission = "security.users.reset_password";

    /// <summary>Permiso/rol que concede ámbito global (cualquier tenant).</summary>
    public const string SuperAdminRole = "SuperAdmin";
    public const string GlobalResetPermission = "security.users.reset_password.all";

    public async Task HandleAsync(AdminResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var email = command.TargetEmail?.Trim() ?? string.Empty;
        if (email.Length == 0)
            throw new TargetUserNotFoundException();

        var target = await userAccountRepository.FindActiveTargetByEmailAsync(email, cancellationToken);
        if (target is null)
            throw new TargetUserNotFoundException();

        EnsureScope(command, target);

        var temporaryPassword = temporaryPasswordGenerator.Generate();
        var hash = passwordHasher.Hash(temporaryPassword);

        await userAccountRepository.UpdatePasswordHashAsync(
            target.UserId, hash, DateTimeOffset.UtcNow, mustChangePassword: true, cancellationToken);

        var message = new EmailMessage(
            target.Email,
            target.DisplayName,
            "Tu contraseña fue restablecida — FLIT",
            BuildBody(target.DisplayName, temporaryPassword));

        await emailSender.SendAsync(message, cancellationToken);
    }

    private static void EnsureScope(AdminResetPasswordCommand command, AdminTargetUser target)
    {
        var isSuperAdmin =
            string.Equals(command.CallerRoleCode, SuperAdminRole, StringComparison.OrdinalIgnoreCase)
            || command.CallerPermissions.Contains(GlobalResetPermission);

        if (isSuperAdmin)
            return;

        var hasPermission = command.CallerPermissions.Contains(ResetPermission);
        var sameTenant = command.CallerTenantId is not null && target.TenantId == command.CallerTenantId;

        if (!hasPermission || !sameTenant)
            throw new AdminScopeException();
    }

    private static string BuildBody(string displayName, string temporaryPassword)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "usuario" : displayName;
        return $"""
            <p>Hola {System.Net.WebUtility.HtmlEncode(name)},</p>
            <p>Un administrador restableció tu contraseña en FLIT. Tu contraseña temporal es:</p>
            <p><strong>{System.Net.WebUtility.HtmlEncode(temporaryPassword)}</strong></p>
            <p>Por seguridad, deberás definir una nueva contraseña la próxima vez que inicies sesión.</p>
            <p>— Equipo FLIT</p>
            """;
    }
}
