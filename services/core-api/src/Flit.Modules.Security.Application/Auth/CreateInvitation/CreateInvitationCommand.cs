namespace Flit.Modules.Security.Application.Auth.CreateInvitation;

public sealed record CreateInvitationCommand(
    Guid TenantId,
    string Email,
    Guid RoleId,
    Guid InvitedBy);

public sealed record InvitationCreatedResult(Guid InvitationId, string Email);
