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

    public Task<bool> UserExistsWithEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Email == email && u.DeletedAt == null, cancellationToken);

    public Task<bool> RoleExistsInTenantAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken)
    {
        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id). El rol
        // asignable en una invitación ya no se valida contra el tenant, sino contra el catálogo
        // global (existe, activo, no borrado). tenantId se conserva en la firma de
        // IInvitationRepository para no romper otros consumidores fuera de esta HU.
        _ = tenantId;
        return db.Roles.AnyAsync(
            x => x.Id == roleId && x.IsActive && x.DeletedAt == null,
            cancellationToken);
    }

    public async Task<Guid> CreateAsync(UserInvitationData invitation, CancellationToken cancellationToken)
    {
        var entity = new UserInvitation
        {
            Id = Guid.CreateVersion7(),
            TenantId = invitation.TenantId,
            Email = invitation.Email,
            FullName = invitation.FullName,
            // El primer rol seleccionado queda como "primario" en la columna singular (HU #10506:
            // se conserva por compatibilidad con consumidores que aún leen un único RoleId; la
            // fuente completa de N roles es la tabla puente invitation_roles).
            RoleId = invitation.RoleIds.Count > 0 ? invitation.RoleIds[0] : null,
            TokenHash = invitation.TokenHash,
            Status = "pending",
            InvitedBy = invitation.InvitedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = 0,
        };

        db.UserInvitations.Add(entity);

        foreach (var roleId in invitation.RoleIds)
        {
            db.InvitationRoles.Add(new InvitationRole
            {
                Id = Guid.CreateVersion7(),
                TenantId = invitation.TenantId,
                InvitationId = entity.Id,
                RoleId = roleId,
                CreatedAt = entity.CreatedAt,
                RowVersion = 0,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task<PendingInvitation?> FindPendingByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        var entity = await db.UserInvitations
            .AsNoTracking()
            .Where(x => x.TokenHash == tokenHash && x.Status == "pending")
            .Select(x => new { x.Id, x.TenantId, x.Email, x.FullName, x.InvitedBy })
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
            return null;

        var roleIds = await db.InvitationRoles
            .AsNoTracking()
            .Where(r => r.InvitationId == entity.Id)
            .Select(r => r.RoleId)
            .ToListAsync(cancellationToken);

        return new PendingInvitation(entity.Id, entity.TenantId, entity.Email, entity.FullName, roleIds, entity.InvitedBy);
    }

    public async Task<IReadOnlyList<PendingInvitationSummary>> ListPendingByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return await db.UserInvitations
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == "pending")
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PendingInvitationSummary(x.Id, x.Email, x.FullName, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<InvitationForResend?> FindForResendAsync(
        Guid invitationId, Guid? scopeTenantId, CancellationToken cancellationToken)
    {
        var query = db.UserInvitations.AsNoTracking().Where(x => x.Id == invitationId);

        if (scopeTenantId is { } tenantId)
            query = query.Where(x => x.TenantId == tenantId);

        return await query
            .Select(x => new InvitationForResend(x.Id, x.Email, x.FullName, x.Status, x.LastSentAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateResendAsync(
        Guid invitationId, string tokenHash, DateTimeOffset lastSentAt, Guid resentBy, CancellationToken cancellationToken)
    {
        var entity = await db.UserInvitations.FirstAsync(x => x.Id == invitationId, cancellationToken);

        entity.TokenHash = tokenHash;
        entity.LastSentAt = lastSentAt;
        entity.UpdatedAt = lastSentAt;
        entity.UpdatedBy = resentBy;

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<InvitationStatusInfo?> FindByIdAsync(Guid invitationId, CancellationToken cancellationToken) =>
        db.UserInvitations
            .AsNoTracking()
            .Where(x => x.Id == invitationId)
            .Select(x => new InvitationStatusInfo(x.Id, x.TenantId, x.Status))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task CancelAsync(Guid invitationId, Guid cancelledBy, CancellationToken cancellationToken)
    {
        var entity = await db.UserInvitations
            .FirstOrDefaultAsync(x => x.Id == invitationId, cancellationToken);

        if (entity is null)
            return;

        var now = DateTimeOffset.UtcNow;
        entity.Status = "cancelled";
        entity.UpdatedAt = now;
        entity.UpdatedBy = cancelledBy;
        entity.DeletedAt = now;
        entity.DeletedBy = cancelledBy;

        await db.SaveChangesAsync(cancellationToken);
    }
}
