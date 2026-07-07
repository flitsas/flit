namespace Flit.Modules.Security.Domain.Auth;

public interface IUserActivationRepository
{
    Task ActivateAsync(ActivationData data, CancellationToken cancellationToken);
}

/// <summary>
/// HU #10506 AC4: <c>RoleIds</c> reemplaza el <c>RoleId?</c> nullable — la activación crea una
/// asignación <c>UserRoleAssignment</c> por cada rol de la invitación (siempre al menos uno).
/// </summary>
public sealed record ActivationData(
    Guid InvitationId,
    string Email,
    string FullName,
    string PasswordHash,
    Guid TenantId,
    IReadOnlyList<Guid> RoleIds,
    Guid InvitedBy,
    DateTimeOffset ActivatedAt);
