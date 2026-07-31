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

        var link = BuildResetLink(options.ResetUrlBase, token.RawToken);
        var message = new EmailMessage(
            user.Email,
            user.DisplayName,
            "Recuperación de contraseña — FLIT",
            BuildHtmlBody(user.DisplayName, link, options.TokenLifetimeMinutes));

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

    private static string BuildResetLink(string resetUrlBase, string rawToken)
    {
        var separator = resetUrlBase.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{resetUrlBase}{separator}token={Uri.EscapeDataString(rawToken)}";
    }

    private static string BuildHtmlBody(string displayName, string link, int lifetimeMinutes)
    {
        var greetingName = string.IsNullOrWhiteSpace(displayName) ? "usuario" : displayName;
        return $"""
            <p>Hola {System.Net.WebUtility.HtmlEncode(greetingName)},</p>
            <p>Recibimos una solicitud para restablecer tu contraseña en FLIT.
            Haz clic en el siguiente enlace para crear una nueva contraseña:</p>
            <p><a href="{link}">Restablecer mi contraseña</a></p>
            <p>El enlace caduca en {lifetimeMinutes} minutos. Si no solicitaste este cambio,
            puedes ignorar este mensaje; tu contraseña seguirá siendo la misma.</p>
            <p>— Equipo FLIT</p>
            """;
    }
}
