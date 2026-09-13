namespace Flit.Admin.Application.Companies.Invitations;

/// <summary>HU #12354 AC3 — valida roleIds contra la lista blanca por identificador.</summary>
public interface IGroupHeadInvitationRolePolicy
{
    Task<IReadOnlySet<Guid>> GetAllowedRoleIdsAsync(CancellationToken cancellationToken = default);

    Task<bool> IsAllowedRoleIdAsync(Guid roleId, CancellationToken cancellationToken = default);
}
