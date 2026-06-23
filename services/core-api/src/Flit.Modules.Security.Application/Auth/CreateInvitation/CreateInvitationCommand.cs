namespace Flit.Modules.Security.Application.Auth.CreateInvitation;

public sealed record CreateInvitationCommand(
    Guid TenantId,
    string Email,
    string FullName,
    Guid? RoleId,
    Guid InvitedBy);

public sealed record InvitationCreatedResult(Guid InvitationId, string Email, bool EmailSent = true);
