namespace Flit.Modules.Security.Domain.Auth;

public interface IInvitationRepository
{
    Task<bool> ExistsPendingAsync(Guid tenantId, string email, CancellationToken cancellationToken);

    Task<bool> RoleExistsInTenantAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(UserInvitationData invitation, CancellationToken cancellationToken);
}

public sealed record UserInvitationData(
    Guid TenantId,
    string Email,
    Guid RoleId,
    string TokenHash,
    Guid InvitedBy);
