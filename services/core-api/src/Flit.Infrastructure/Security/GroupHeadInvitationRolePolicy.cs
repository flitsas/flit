using Flit.Admin.Application.Companies.Invitations;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Security;

/// <summary>Resuelve los ids de rol permitidos desde el catálogo global (HU #12354 AC3).</summary>
internal sealed class GroupHeadInvitationRolePolicy(FlitDbContext db) : IGroupHeadInvitationRolePolicy
{
    public async Task<IReadOnlySet<Guid>> GetAllowedRoleIdsAsync(CancellationToken cancellationToken = default)
    {
        var allowedCodes = GroupHeadChildInvitationRoles.AllowedCodes;
        var forbidden = GroupHeadChildInvitationRoles.ForbiddenCodes;

        var ids = await db.Roles
            .AsNoTracking()
            .Where(r => r.IsActive && r.DeletedAt == null && r.TargetEntityType == "COMPANY")
            .Where(r => allowedCodes.Contains(r.Code))
            .Where(r => !forbidden.Contains(r.Code))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ids.ToHashSet();
    }

    public async Task<bool> IsAllowedRoleIdAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedRoleIdsAsync(cancellationToken).ConfigureAwait(false);
        return allowed.Contains(roleId);
    }
}
