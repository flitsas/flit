using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Security.Application.Auth.CreateInvitation;

public sealed partial class CreateInvitationHandler(
    IInvitationRepository invitationRepository,
    IUserManagementRepository userManagementRepository,
    ISecureTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    InvitationOptions options,
    ILogger<CreateInvitationHandler> logger)
{
    public async Task<InvitationCreatedResult> HandleAsync(
        CreateInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var email = command.Email?.Trim() ?? string.Empty;

        // AC5 — seleccionar al menos un rol es OBLIGATORIO al invitar (ya no se permite
        // "sin rol asignado").
        var roleIds = command.RoleIds?.Distinct().ToList() ?? [];
        if (roleIds.Count == 0)
            throw new NoRolesSelectedException();

        // HU #10623 AC4 — el correo pertenece a una cuenta soft-deleted: uq_users_email es un
        // índice único GLOBAL (no parcial por deleted_at), así que ese correo sigue "ocupado" en
        // BD aunque IInvitationRepository.UserExistsWithEmailAsync (que SÍ filtra DeletedAt ==
        // null) reporte que "no existe". Sin este chequeo la invitación se crearía igual y solo
        // fallaría al activarla, con un error crudo de constraint de BD.
        var existingByEmail = await userManagementRepository.FindByEmailIncludingDeletedAsync(email, cancellationToken);
        if (existingByEmail is { IsDeleted: true })
            throw new UserEmailBelongsToDeletedAccountException();

        foreach (var roleId in roleIds)
        {
            var roleExists = await invitationRepository.RoleExistsInTenantAsync(
                command.TenantId, roleId, cancellationToken);
            if (!roleExists)
                throw new RoleNotFoundException();
        }

        var hasPending = await invitationRepository.ExistsPendingAsync(
            command.TenantId, email, cancellationToken);
        if (hasPending)
            throw new InvitationAlreadyPendingException();

        var userExists = await invitationRepository.UserExistsWithEmailAsync(email, cancellationToken);
        if (userExists)
            throw new UserAlreadyExistsException();

        var token = tokenGenerator.Generate();

        var invitationId = await invitationRepository.CreateAsync(
            new UserInvitationData(command.TenantId, email, command.FullName, roleIds, token.TokenHash, command.InvitedBy),
            cancellationToken);

        var link = BuildActivateLink(options.ActivateUrlBase, token.RawToken);
        var message = new EmailMessage(
            email,
            email,
            "Invitación a FLIT — Activa tu cuenta",
            BuildHtmlBody(command.FullName, link));

        LogActivationLinkDev(logger, link);

        var emailSent = true;
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            LogEmailFailed(logger, invitationId, ex);
            emailSent = false;
        }

        return new InvitationCreatedResult(invitationId, email, emailSent);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[retryable] Activation email failed for invitation {InvitationId}. Invitation remains pending.")]
    private static partial void LogEmailFailed(ILogger logger, Guid invitationId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[DEV] Activation link (use this to test locally): {Link}")]
    private static partial void LogActivationLinkDev(ILogger logger, string link);

    private static string BuildActivateLink(string activateUrlBase, string rawToken)
    {
        var separator = activateUrlBase.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{activateUrlBase}{separator}token={Uri.EscapeDataString(rawToken)}";
    }

    private static string BuildHtmlBody(string fullName, string link) => $"""
        <p>Hola {System.Net.WebUtility.HtmlEncode(fullName)},</p>
        <p>Has sido invitado a unirte a FLIT.</p>
        <p>Haz clic en el siguiente enlace para crear tu contraseña y activar tu cuenta:</p>
        <p><a href="{link}">Activar mi cuenta</a></p>
        <p>Si no esperabas esta invitación, puedes ignorar este mensaje.</p>
        <p>— Equipo FLIT</p>
        """;
}
