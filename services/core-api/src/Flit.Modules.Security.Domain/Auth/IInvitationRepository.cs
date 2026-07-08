namespace Flit.Modules.Security.Domain.Auth;

public interface IInvitationRepository
{
    Task<bool> ExistsPendingAsync(Guid tenantId, string email, CancellationToken cancellationToken);

    Task<bool> UserExistsWithEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> RoleExistsInTenantAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(UserInvitationData invitation, CancellationToken cancellationToken);

    Task<PendingInvitation?> FindPendingByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingInvitationSummary>> ListPendingByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// HU #10506 AC4/AC5: <c>RoleIds</c> reemplaza el <c>RoleId?</c> nullable — siempre tiene al
/// menos un elemento (validado en <c>CreateInvitationHandler</c> antes de llegar aquí).
/// </summary>
public sealed record UserInvitationData(
    Guid TenantId,
    string Email,
    string FullName,
    IReadOnlyList<Guid> RoleIds,
    string TokenHash,
    Guid InvitedBy);

public sealed record PendingInvitation(
    Guid InvitationId,
    Guid TenantId,
    string Email,
    string FullName,
    IReadOnlyList<Guid> RoleIds,
    Guid InvitedBy);

public sealed record PendingInvitationSummary(
    Guid InvitationId,
    string Email,
    string FullName,
    DateTimeOffset CreatedAt);
