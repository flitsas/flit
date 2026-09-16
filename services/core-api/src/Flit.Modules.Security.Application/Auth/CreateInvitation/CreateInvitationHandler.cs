using Flit.Modules.Security.Application.Auth.Network;
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
    INetworkUrlBaseResolver urlBaseResolver,
    ILogger<CreateInvitationHandler> logger,
    IEmailThemeResolver? themeResolver = null)
{
    // HU #12428 — parámetro opcional (patrón NullBrandingCacheInvalidator/NullEmailThemeResolver):
    // los tests que no ejercitan esta historia no cambian su construcción del handler.
    private readonly IEmailThemeResolver _themeResolver = themeResolver ?? NullEmailThemeResolver.Instance;

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

        // HU #12423 AC1 — el enlace de activación usa el dominio de la red del tenant al que se
        // invita (cabeza o hija); sin dominio activo, la base configurada literal (AC4).
        var activateUrlBase = await urlBaseResolver
            .ForTenantAsync(command.TenantId, options.ActivateUrlBase, cancellationToken)
            .ConfigureAwait(false);
        var link = InvitationEmailTemplate.BuildActivateLink(activateUrlBase, token.RawToken);
        // HU #12428 AC1/AC8 — tema por la red del tenant destino (cabeza, hija o FLIT).
        var theme = await _themeResolver.ResolveAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        var composed = InvitationEmailTemplate.Compose(command.FullName, link, theme: theme);
        // HU #11363 AC1 — id estable del catálogo (TemplateIds.Invitation en Flit.Infrastructure);
        // comparte plantilla con ResendInvitationHandler (dos disparadores, una sola entrada).
        var message = new EmailMessage(command.TenantId, "security.invitation", email, email, composed.Subject, composed.HtmlBody)
        {
            ThemeKind = theme.KindWireValue,
            ThemeVersion = theme.IsBrand ? theme.Version : null,
        };

        LogActivationLinkDev(logger, link);

        // HU #11358 AC2/AC3 — el puerto ya no lanza por un fallo de transporte: el resultado
        // tipado reemplaza el try/catch.
        var sendResult = await emailSender.SendAsync(message, cancellationToken);
        if (!sendResult.Success)
            LogEmailFailed(logger, invitationId, sendResult.Outcome);

        return new InvitationCreatedResult(invitationId, email, sendResult.Success);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[retryable] Activation email failed for invitation {InvitationId}. Invitation remains pending. Cause: {Outcome}.")]
    private static partial void LogEmailFailed(ILogger logger, Guid invitationId, EmailSendOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[DEV] Activation link (use this to test locally): {Link}")]
    private static partial void LogActivationLinkDev(ILogger logger, string link);
}
