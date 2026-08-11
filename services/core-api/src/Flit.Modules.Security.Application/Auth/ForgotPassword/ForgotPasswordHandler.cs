using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Security.Application.Auth.ForgotPassword;

/// <summary>
/// Solicitud de recuperación (HU #10169, AC1/AC2). Si el email corresponde a un usuario
/// activo, genera un token de un solo uso, lo persiste (solo el hash) y envía el enlace
/// por correo. En cualquier otro caso no hace nada: el endpoint responde 202 genérico
/// para no filtrar la existencia del email (anti-enumeración).
/// HU #11358 AC3 — un fallo del transporte de correo (resultado tipado, no excepción) NO
/// interrumpe el flujo: el token ya quedó persistido y el endpoint sigue respondiendo el mismo
/// 202 genérico sin importar si el correo salió o no (mismo comportamiento anti-enumeración).
/// </summary>
public sealed partial class ForgotPasswordHandler(
    IUserAccountRepository userAccountRepository,
    IPasswordResetTokenRepository tokenRepository,
    ISecureTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    PasswordRecoveryOptions options,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext,
    ILogger<ForgotPasswordHandler> logger)
{
    private const string Purpose = "password_reset";

    public async Task HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email?.Trim() ?? string.Empty;
        if (email.Length == 0)
            return;

        var user = await userAccountRepository.FindActiveByEmailAsync(email, cancellationToken);
        if (user is null)
            return;

        var token = tokenGenerator.Generate();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(options.TokenLifetimeMinutes);

        await tokenRepository.CreateAsync(user.UserId, token.TokenHash, Purpose, expiresAt, cancellationToken);

        var link = ForgotPasswordEmailTemplate.BuildResetLink(options.ResetUrlBase, token.RawToken);
        var composed = ForgotPasswordEmailTemplate.Compose(user.DisplayName, link, options.TokenLifetimeMinutes);
        // HU #11363 AC1 — id estable del catálogo (NotificationTemplateCatalog.TemplateIds.ForgotPassword
        // en Flit.Infrastructure); literal a mano porque este proyecto no depende de Infrastructure.
        var message = new EmailMessage(
            user.TenantId, "security.forgot-password", user.Email, user.DisplayName, composed.Subject, composed.HtmlBody);

        var sendResult = await emailSender.SendAsync(message, cancellationToken);
        if (!sendResult.Success)
            LogEmailFailed(logger, user.UserId, sendResult.Outcome);

        // HU #10678 — sin PII/token en el rastro: solo desenlace + actor/afectado (el mismo usuario).
        await auditWriter.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                AuditVocabulary.Modules.Authentication,
                EntityName: "user",
                AuditVocabulary.Operations.ForgotPassword,
                AuditVocabulary.Results.Success,
                ErrorCode: null,
                ActorUserId: user.UserId,
                TargetEntityType: "USER",
                TargetEntityId: user.UserId,
                auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No fue posible enviar el correo de recuperación de contraseña para el usuario {UserId}. Cause: {Outcome}.")]
    private static partial void LogEmailFailed(ILogger logger, Guid userId, EmailSendOutcome outcome);
}
