using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.ForgotPassword;

/// <summary>
/// Solicitud de recuperación (HU #10169, AC1/AC2). Si el email corresponde a un usuario
/// activo, genera un token de un solo uso, lo persiste (solo el hash) y envía el enlace
/// por correo. En cualquier otro caso no hace nada: el endpoint responde 202 genérico
/// para no filtrar la existencia del email (anti-enumeración).
/// </summary>
public sealed class ForgotPasswordHandler(
    IUserAccountRepository userAccountRepository,
    IPasswordResetTokenRepository tokenRepository,
    ISecureTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    PasswordRecoveryOptions options,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext)
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
        var message = new EmailMessage(user.Email, user.DisplayName, composed.Subject, composed.HtmlBody);

        await emailSender.SendAsync(message, cancellationToken);

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

}
