using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class UserActivationRepository(FlitDbContext db) : IUserActivationRepository
{
    public async Task ActivateAsync(ActivationData data, CancellationToken cancellationToken)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = data.Email,
            DisplayName = data.Email,
            Status = "active",
            CreatedAt = data.ActivatedAt,
        };
        db.Users.Add(user);

        db.UserCredentials.Add(new UserCredential
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            PasswordHash = data.PasswordHash,
            MustChangePassword = false,
            PasswordChangedAt = data.ActivatedAt,
            CreatedAt = data.ActivatedAt,
        });

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = data.TenantId,
            UserId = user.Id,
            RoleId = data.RoleId,
            AssignedAt = data.ActivatedAt,
            AssignedBy = data.InvitedBy,
            CreatedAt = data.ActivatedAt,
        });

        // Track invitation update through Change Tracker → single atomic SaveChanges
        var invitation = await db.UserInvitations.FindAsync([data.InvitationId], cancellationToken);
        if (invitation is not null)
        {
            invitation.Status = "accepted";
            invitation.AcceptedAt = data.ActivatedAt;
            invitation.UpdatedAt = data.ActivatedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
