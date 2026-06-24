namespace Flit.Modules.Security.Domain.Auth;

public interface IInvitationRepository
{
    Task<bool> ExistsPendingAsync(Guid tenantId, string email, CancellationToken cancellationToken);

    Task<bool> RoleExistsInTenantAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(UserInvitationData invitation, CancellationToken cancellationToken);

    Task<PendingInvitation?> FindPendingByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingInvitationSummary>> ListPendingByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record UserInvitationData(
    Guid TenantId,
    string Email,
    string FullName,
    Guid? RoleId,
    string TokenHash,
    Guid InvitedBy);

public sealed record PendingInvitation(
    Guid InvitationId,
    Guid TenantId,
    string Email,
    string FullName,
    Guid? RoleId,
    Guid InvitedBy);

public sealed record PendingInvitationSummary(
    Guid InvitationId,
    string Email,
    string FullName,
    DateTimeOffset CreatedAt);
