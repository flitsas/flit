using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Security.Application.Auth.AdminResetPassword;

/// <summary>
/// Reset administrativo de contraseña (HU #10170, AC1, RF21-RF23). Un administrador con
/// ámbito sobre el usuario genera una contraseña temporal, actualiza el hash, marca
/// <c>must_change_password</c> y notifica por correo al usuario.
///
/// Ámbito: Superadmin (rol o permiso global) puede sobre cualquier tenant; un admin de
/// compañía requiere el permiso de reset y que el usuario pertenezca a su mismo tenant.
///
/// HU #11358 AC3 — un fallo del transporte de correo (resultado tipado, no excepción) NO
/// interrumpe el flujo: la contraseña ya quedó actualizada y el endpoint responde igual,
/// sin exponer la causa técnica del fallo de envío al llamador.
/// </summary>
public sealed partial class AdminResetPasswordHandler(
    IUserAccountRepository userAccountRepository,
    ITemporaryPasswordGenerator temporaryPasswordGenerator,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext,
    ILogger<AdminResetPasswordHandler> logger)
{
    /// <summary>Permiso requerido para resetear contraseñas dentro del propio tenant.</summary>
    public const string ResetPermission = "security.users.reset_password";

    /// <summary>Permiso/rol que concede ámbito global (cualquier tenant).</summary>
    public const string SuperAdminRole = "SuperAdmin";

    /// <summary>Rol de administrador de compañía gestora (mismo tenant).</summary>
    public const string AdminCompanyRole = "AdminCompany";

    public const string GlobalResetPermission = "security.users.reset_password.all";

    public async Task HandleAsync(AdminResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var email = command.TargetEmail?.Trim() ?? string.Empty;
        if (email.Length == 0)
        {
            await AuditAsync(command, null, AuditVocabulary.Results.Failure, "user_not_found", cancellationToken)
                .ConfigureAwait(false);
            throw new TargetUserNotFoundException();
        }

        var target = await userAccountRepository.FindActiveTargetByEmailAsync(email, cancellationToken);
        if (target is null)
        {
            await AuditAsync(command, null, AuditVocabulary.Results.Failure, "user_not_found", cancellationToken)
                .ConfigureAwait(false);
            throw new TargetUserNotFoundException();
        }

        try
        {
            EnsureScope(command, target);
        }
        catch (AdminScopeException)
        {
            await AuditAsync(command, target.UserId, AuditVocabulary.Results.Failure, "forbidden_scope", cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        var temporaryPassword = temporaryPasswordGenerator.Generate();
        var hash = passwordHasher.Hash(temporaryPassword);

        await userAccountRepository.UpdatePasswordHashAsync(
            target.UserId, hash, DateTimeOffset.UtcNow, mustChangePassword: true, cancellationToken);

        var composed = AdminResetPasswordEmailTemplate.Compose(target.DisplayName, temporaryPassword);
        var message = new EmailMessage(target.TenantId, target.Email, target.DisplayName, composed.Subject, composed.HtmlBody);

        var sendResult = await emailSender.SendAsync(message, cancellationToken);
        if (!sendResult.Success)
            LogEmailFailed(logger, target.UserId, sendResult.Outcome);

        await AuditAsync(command, target.UserId, AuditVocabulary.Results.Success, null, cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No fue posible enviar el correo de reset administrativo para el usuario {UserId}. Cause: {Outcome}.")]
    private static partial void LogEmailFailed(ILogger logger, Guid userId, EmailSendOutcome outcome);

    // HU #10678 — sin contraseñas en el rastro: actor = admin que ejecuta, afectado = usuario objetivo.
    private async Task AuditAsync(
        AdminResetPasswordCommand command,
        Guid? targetUserId,
        string result,
        string? errorCode,
        CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(
            new AdminAuditEntry(
                command.CallerTenantId,
                TenantType: null,
                AuditVocabulary.Modules.Authentication,
                EntityName: "user",
                AuditVocabulary.Operations.AdminResetPassword,
                result,
                errorCode,
                ActorUserId: null,
                TargetEntityType: targetUserId is null ? null : "USER",
                TargetEntityId: targetUserId,
                auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);

    private static void EnsureScope(AdminResetPasswordCommand command, AdminTargetUser target)
    {
        var isSuperAdmin =
            string.Equals(command.CallerRoleCode, SuperAdminRole, StringComparison.OrdinalIgnoreCase)
            || command.CallerPermissions.Contains(GlobalResetPermission);

        if (isSuperAdmin)
            return;

        var sameTenant = command.CallerTenantId is not null && target.TenantId == command.CallerTenantId;
        if (!sameTenant)
            throw new AdminScopeException();

        // AdminCompany del mismo tenant (paridad de producto) o permiso explícito en el JWT.
        var isAdminCompany = string.Equals(
            command.CallerRoleCode, AdminCompanyRole, StringComparison.OrdinalIgnoreCase);
        var hasPermission = command.CallerPermissions.Contains(ResetPermission);

        if (!isAdminCompany && !hasPermission)
            throw new AdminScopeException();
    }

}
