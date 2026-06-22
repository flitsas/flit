using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class InvitationRepository(FlitDbContext db) : IInvitationRepository
{
    public Task<bool> ExistsPendingAsync(Guid tenantId, string email, CancellationToken cancellationToken) =>
        db.UserInvitations.AnyAsync(
            x => x.TenantId == tenantId && x.Email == email && x.Status == "pending",
            cancellationToken);

    public Task<bool> RoleExistsInTenantAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken) =>
        db.Roles.AnyAsync(
            x => x.TenantId == tenantId && x.Id == roleId && x.DeletedAt == null,
            cancellationToken);

    public async Task<Guid> CreateAsync(UserInvitationData invitation, CancellationToken cancellationToken)
    {
        var entity = new UserInvitation
        {
            Id = Guid.CreateVersion7(),
            TenantId = invitation.TenantId,
            Email = invitation.Email,
            RoleId = invitation.RoleId,
            TokenHash = invitation.TokenHash,
            Status = "pending",
            InvitedBy = invitation.InvitedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = 0,
        };

        db.UserInvitations.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }
}
