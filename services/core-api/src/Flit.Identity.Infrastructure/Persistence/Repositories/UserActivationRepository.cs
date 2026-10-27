using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class UserActivationRepository(IIdentityDb db) : IUserActivationRepository
{
    public async Task ActivateAsync(ActivationData data, CancellationToken cancellationToken)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = data.Email,
            DisplayName = string.IsNullOrWhiteSpace(data.FullName) ? data.Email : data.FullName,
            Status = "active",
            HomeTenantId = data.TenantId,
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

        // HU #10506 AC4: una fila UserRoleAssignment POR CADA rol de la invitación (antes, a lo
        // sumo una). RoleIds siempre trae al menos un elemento (AC5, validado en
        // CreateInvitationHandler antes de crear la invitación).
        foreach (var roleId in data.RoleIds)
        {
            db.UserRoleAssignments.Add(new UserRoleAssignment
            {
                Id = Guid.CreateVersion7(),
                TenantId = data.TenantId,
                UserId = user.Id,
                RoleId = roleId,
                AssignedAt = data.ActivatedAt,
                AssignedBy = data.InvitedBy,
                CreatedAt = data.ActivatedAt,
            });
        }

        // Track invitation update through Change Tracker → single atomic SaveChanges
        var invitation = await db.UserInvitations.FindAsync([data.InvitationId], cancellationToken);
        if (invitation is not null)
        {
            invitation.Status = "accepted";
            invitation.AcceptedAt = data.ActivatedAt;
            invitation.UpdatedAt = data.ActivatedAt;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsLiveEmailUniqueViolation(ex))
        {
            // HU #13811 — red de la carrera: el handler ya revalida con UserExistsWithEmailAsync, pero dos
            // activaciones simultáneas del mismo correo chocan aquí. Nada se guardó (SaveChanges es atómico);
            // se descarta lo rastreado para no arrastrarlo y se responde 409 en vez de 500.
            db.ChangeTracker.Clear();
            throw new UserAlreadyExistsException();
        }
    }

    private const string LiveEmailUniqueIndex = "uq_users_email";

    private static bool IsLiveEmailUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation
        && pg.ConstraintName == LiveEmailUniqueIndex;
}
