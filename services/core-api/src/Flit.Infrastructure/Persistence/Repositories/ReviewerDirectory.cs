using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Resuelve nombre, correo y rol de los usuarios revisores (identity.users + asignaciones de rol activas).
/// Lectura cross-tenant a propósito (el Super Admin ve revisores de cualquier compañía); el caller exige Super Admin.
/// El rol es el <c>Role.Name</c> de la primera asignación activa en orden estable por nombre.
/// </summary>
internal sealed class ReviewerDirectory(FlitDbContext db) : IReviewerDirectory
{
    public Task<IReadOnlyDictionary<Guid, ReviewerInfo>> ResolveAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        return CrossTenantRead.ExecuteAsync(db, () => ResolveCoreAsync(db, userIds, ct), ct);
    }

    /// <summary>Resolución sin abrir transacción propia: para llamarla desde una lectura que ya corre en <see cref="CrossTenantRead"/>.</summary>
    internal static async Task<IReadOnlyDictionary<Guid, ReviewerInfo>> ResolveCoreAsync(
        FlitDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        var result = ids.ToDictionary(id => id, _ => new ReviewerInfo(null, null, null));
        if (ids.Count == 0)
            return result;

        var users = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct).ConfigureAwait(false);

        var roles = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where ids.Contains(a.UserId) && a.DeletedAt == null
            select new { a.UserId, r.Name }).ToListAsync(ct).ConfigureAwait(false);

        var roleByUser = roles
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).OrderBy(n => n, StringComparer.Ordinal).First());

        foreach (var u in users)
        {
            result[u.Id] = new ReviewerInfo(
                string.IsNullOrWhiteSpace(u.DisplayName) ? null : u.DisplayName,
                string.IsNullOrWhiteSpace(u.Email) ? null : u.Email,
                roleByUser.TryGetValue(u.Id, out var role) ? role : null);
        }

        return result;
    }
}
