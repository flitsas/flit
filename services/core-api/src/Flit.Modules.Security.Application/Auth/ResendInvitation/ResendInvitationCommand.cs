namespace Flit.Modules.Security.Application.Auth.ResendInvitation;

/// <summary>
/// HU #10625: reenvío de invitación pendiente. <c>ScopeTenantId</c> replica el mismo alcance
/// de autorización que <c>CreateInvitationCommand</c>: <c>null</c> cuando el caller es
/// SuperAdmin (puede reenviar cualquier invitación del sistema), o el tenant del caller
/// cuando es AdminCompany/ot_admin (solo puede reenviar invitaciones de su propio tenant).
/// </summary>
public sealed record ResendInvitationCommand(Guid InvitationId, Guid? ScopeTenantId, Guid ResentBy);

public sealed record ResendInvitationResult(Guid InvitationId, string Email, bool EmailSent = true);
