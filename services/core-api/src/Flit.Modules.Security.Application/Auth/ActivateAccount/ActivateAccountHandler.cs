using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Security.Application.Auth.ActivateAccount;

public sealed partial class ActivateAccountHandler(
    IInvitationRepository invitationRepository,
    ISecureTokenGenerator tokenGenerator,
    IUserActivationRepository userActivationRepository,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext,
    ITenantNetworkMembership networkMembership,
    IDomainContextAccessor domainContext,
    ILogger<ActivateAccountHandler> logger,
    IEmailThemeResolver? themeResolver = null)
{
    private readonly IEmailThemeResolver _themeResolver = themeResolver ?? NullEmailThemeResolver.Instance;

    public async Task<AccountActivatedResult> HandleAsync(
        ActivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenGenerator.HashToken(command.Token);

        var invitation = await invitationRepository.FindPendingByTokenHashAsync(tokenHash, cancellationToken);
        if (invitation is null)
        {
            await AuditAsync(null, AuditVocabulary.Results.Failure, "invitation_invalid", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidInvitationTokenException();
        }

        // HU #12423 AC5 — el enlace es de la red de otro tenant (o de FLIT para un tenant de red):
        // mismo error genérico de token inválido de hoy, sin revelar a qué red pertenece.
        var domainCoherent = await NetworkDomainCoherence
            .IsCoherentAsync(networkMembership, domainContext, invitation.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!domainCoherent)
        {
            await AuditAsync(invitation, AuditVocabulary.Results.Failure, "invitation_invalid", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidInvitationTokenException();
        }

        if (!PasswordPolicy.IsCompliant(command.Password))
        {
            await AuditAsync(invitation, AuditVocabulary.Results.Failure, "weak_password", cancellationToken)
                .ConfigureAwait(false);
            throw new WeakPasswordException();
        }

        var passwordHash = passwordHasher.Hash(command.Password);
        var activatedAt = DateTimeOffset.UtcNow;

        await userActivationRepository.ActivateAsync(
            new ActivationData(
                invitation.InvitationId,
                invitation.Email,
                invitation.FullName,
                passwordHash,
                invitation.TenantId,
                invitation.RoleIds,
                invitation.InvitedBy,
                activatedAt),
            cancellationToken);

        await AuditAsync(invitation, AuditVocabulary.Results.Success, null, cancellationToken).ConfigureAwait(false);

        await TrySendWelcomeEmailAsync(invitation, cancellationToken).ConfigureAwait(false);

        return new AccountActivatedResult();
    }

    /// <summary>
    /// HU #11489 H2 — bienvenida tras activación. Fail-open: un fallo de correo no revierte la activación.
    /// </summary>
    private async Task TrySendWelcomeEmailAsync(PendingInvitation invitation, CancellationToken cancellationToken)
    {
        try
        {
            var theme = await _themeResolver.ResolveAsync(invitation.TenantId, cancellationToken).ConfigureAwait(false);
            var composed = WelcomeRegistrationEmailTemplate.Compose(theme: theme);
            var message = new EmailMessage(
                invitation.TenantId,
                "security.welcome-registration",
                invitation.Email,
                invitation.FullName,
                composed.Subject,
                composed.HtmlBody)
            {
                ThemeKind = theme.KindWireValue,
                ThemeVersion = theme.IsBrand ? theme.Version : null,
            };

            var sendResult = await emailSender.SendAsync(message, cancellationToken).ConfigureAwait(false);
            if (!sendResult.Success)
                LogWelcomeEmailFailed(logger, invitation.InvitationId, sendResult.Outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWelcomeEmailException(logger, invitation.InvitationId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No fue posible enviar el correo de bienvenida tras activar la invitación {InvitationId}. Cause: {Outcome}.")]
    private static partial void LogWelcomeEmailFailed(ILogger logger, Guid invitationId, EmailSendOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Excepción al enviar el correo de bienvenida tras activar la invitación {InvitationId}.")]
    private static partial void LogWelcomeEmailException(ILogger logger, Guid invitationId, Exception exception);

    // HU #10678 — sin contraseñas/token en el rastro. El "afectado" es la invitación (el usuario
    // se crea en este mismo paso; su id lo asigna la infraestructura de activación).
    private async Task AuditAsync(
        PendingInvitation? invitation, string result, string? errorCode, CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(
            new AdminAuditEntry(
                invitation?.TenantId,
                TenantType: null,
                AuditVocabulary.Modules.Authentication,
                EntityName: "invitation",
                AuditVocabulary.Operations.ActivateAccount,
                result,
                errorCode,
                ActorUserId: null,
                TargetEntityType: invitation is null ? null : "INVITATION",
                TargetEntityId: invitation?.InvitationId,
                auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);
}
