namespace Flit.Modules.Security.Application.Auth.CancelInvitation;

/// <summary>
/// HU #10627: cancela una invitación pendiente. <c>ScopeTenantId</c> replica el mismo patrón de
/// alcance usado en <c>CreateInvitationCommand</c> — SuperAdmin no restringe por tenant (puede
/// cancelar la invitación de cualquier empresa/OT), mientras que AdminCompany/ot_admin solo
/// puede cancelar invitaciones de su propio tenant.
/// </summary>
public sealed record CancelInvitationCommand(
    Guid InvitationId,
    Guid? ScopeTenantId,
    Guid CancelledBy);
