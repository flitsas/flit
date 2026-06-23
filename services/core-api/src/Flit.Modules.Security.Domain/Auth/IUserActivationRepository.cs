namespace Flit.Modules.Security.Domain.Auth;

public interface IUserActivationRepository
{
    Task ActivateAsync(ActivationData data, CancellationToken cancellationToken);
}

public sealed record ActivationData(
    Guid InvitationId,
    string Email,
    string FullName,
    string PasswordHash,
    Guid TenantId,
    Guid? RoleId,
    Guid InvitedBy,
    DateTimeOffset ActivatedAt);
