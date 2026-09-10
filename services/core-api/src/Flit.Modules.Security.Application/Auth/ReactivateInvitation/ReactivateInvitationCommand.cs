namespace Flit.Modules.Security.Application.Auth.ReactivateInvitation;

/// <summary>
/// HU #11552 / ADR-0048: reactiva una invitación cancelada. <c>ScopeTenantId</c> replica el
/// mismo patrón de alcance que <c>CancelInvitationCommand</c>/<c>ResendInvitationCommand</c> —
/// SuperAdmin no restringe por tenant, AdminCompany/ot_admin solo su propio tenant.
/// </summary>
public sealed record ReactivateInvitationCommand(
    Guid InvitationId,
    Guid? ScopeTenantId,
    Guid ReactivatedBy);

public sealed record ReactivateInvitationResult(Guid InvitationId, string Email, bool EmailSent = true);
