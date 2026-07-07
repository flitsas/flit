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

    public async Task<PendingInvitation?> FindPendingByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        var entity = await db.UserInvitations
            .Where(x => x.TokenHash == tokenHash && x.Status == "pending")
            .Select(x => new PendingInvitation(x.Id, x.TenantId, x.Email, x.FullName, x.RoleId, x.InvitedBy))
            .FirstOrDefaultAsync(cancellationToken);

        return entity;
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
}
