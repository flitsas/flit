using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de la gestión administrativa de perfil de usuario (HU #10621) sobre
/// <c>identity.users</c>.
/// </summary>
public sealed class UserManagementRepository(FlitDbContext db) : IUserManagementRepository
{
    public async Task<UserManagementTarget?> FindTargetAsync(Guid userId, bool includeDeleted, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Where(u => u.Id == userId);
        if (!includeDeleted)
            query = query.Where(u => u.DeletedAt == null);

        return await query
            .Select(u => new UserManagementTarget(
                u.Id,
                u.HomeTenantId ?? Guid.Empty,
                u.Email,
                u.DisplayName,
                u.DeletedAt,
                u.RowVersion))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ExistingUserByEmail?> FindByEmailIncludingDeletedAsync(string email, CancellationToken ct)
    {
        // uq_users_email es un índice único GLOBAL (no parcial por deleted_at): un correo
        // soft-deleted sigue "ocupado" en BD, por eso esta búsqueda NO filtra por DeletedAt
        // (a diferencia de AuthUserRepository.FindByEmailAsync, que sí lo hace para el login).
        return await db.Users
            .AsNoTracking()
            .Where(u => EF.Functions.ILike(u.Email, email))
            .Select(u => new ExistingUserByEmail(u.Id, u.DeletedAt != null))
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? email,
        long expectedRowVersion,
        DateTimeOffset updatedAt,
        Guid? updatedBy,
        CancellationToken ct)
    {
        var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity is null)
            throw new TargetUserNotFoundException();

        // Concurrencia optimista contra la versión que el CALLER leyó (no la que acabamos de
        // traer de BD): al forzar el OriginalValue del concurrency token a expectedRowVersion,
        // el UPDATE que emite EF incluye "WHERE ... AND row_version = expectedRowVersion". Si
        // otro admin ya guardó cambios entre el fetch del formulario y este submit, row_version
        // actual en BD ya no coincide con expectedRowVersion → 0 filas afectadas → EF lanza
        // DbUpdateConcurrencyException (AC4). El trigger tr_users_row_version (BEFORE UPDATE)
        // incrementa row_version en BD de forma independiente de lo que EF intente escribir.
        db.Entry(entity).Property(e => e.RowVersion).OriginalValue = expectedRowVersion;

        if (displayName is not null)
            entity.DisplayName = displayName;
        if (email is not null)
            entity.Email = email;

        entity.UpdatedAt = updatedAt;
        entity.UpdatedBy = updatedBy;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new UserProfileConcurrencyException();
        }
    }
}
