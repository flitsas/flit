namespace Flit.Modules.Security.Application.Auth.CreateInvitation;

/// <summary>
/// HU #10506 AC4/AC5: <c>RoleIds</c> reemplaza el <c>RoleId?</c> nullable — invitar con varios
/// roles simultáneos ya es soportado, y seleccionar al menos un rol es OBLIGATORIO (ya no existe
/// "invitación sin rol asignado").
/// </summary>
public sealed record CreateInvitationCommand(
    Guid TenantId,
    string Email,
    string FullName,
    IReadOnlyList<Guid> RoleIds,
    Guid InvitedBy);

public sealed record InvitationCreatedResult(Guid InvitationId, string Email, bool EmailSent = true);
