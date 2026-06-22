using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.CreateInvitation;

public sealed class CreateInvitationHandler(
    IInvitationRepository invitationRepository,
    ISecureTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    InvitationOptions options)
{
    public async Task<InvitationCreatedResult> HandleAsync(
        CreateInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var email = command.Email?.Trim() ?? string.Empty;

        var roleExists = await invitationRepository.RoleExistsInTenantAsync(
            command.TenantId, command.RoleId, cancellationToken);
        if (!roleExists)
            throw new RoleNotFoundException();

        var hasPending = await invitationRepository.ExistsPendingAsync(
            command.TenantId, email, cancellationToken);
        if (hasPending)
            throw new InvitationAlreadyPendingException();

        var token = tokenGenerator.Generate();

        var invitationId = await invitationRepository.CreateAsync(
            new UserInvitationData(command.TenantId, email, command.RoleId, token.TokenHash, command.InvitedBy),
            cancellationToken);

        var link = BuildActivateLink(options.ActivateUrlBase, token.RawToken);
        var message = new EmailMessage(
            email,
            email,
            "Invitación a FLIT — Activa tu cuenta",
            BuildHtmlBody(link));

        await emailSender.SendAsync(message, cancellationToken);

        return new InvitationCreatedResult(invitationId, email);
    }

    private static string BuildActivateLink(string activateUrlBase, string rawToken)
    {
        var separator = activateUrlBase.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{activateUrlBase}{separator}token={Uri.EscapeDataString(rawToken)}";
    }

    private static string BuildHtmlBody(string link) => $"""
        <p>Has sido invitado a unirte a FLIT.</p>
        <p>Haz clic en el siguiente enlace para crear tu contraseña y activar tu cuenta:</p>
        <p><a href="{link}">Activar mi cuenta</a></p>
        <p>Si no esperabas esta invitación, puedes ignorar este mensaje.</p>
        <p>— Equipo FLIT</p>
        """;
}
