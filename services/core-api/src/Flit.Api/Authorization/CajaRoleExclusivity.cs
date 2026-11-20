using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #14454 (Épica #14210, ADR-0082) — tercera capa del guardián solo-caja: el rol <c>ot_caja</c> solo se reparte por las rutas
/// dedicadas <c>/api/v1/admin/ot/portal-caja/users</c>. Las rutas genéricas de invitación y de asignación de roles lo rechazan, y
/// a un usuario que ya es de caja no le agregan otro rol (así no queda mezclado con un rol de FLIT 2.0 sin que lo decida un SuperAdmin).
/// </summary>
internal static class CajaRoleExclusivity
{
    public const string Code = "CAJA_ROLE_EXCLUSIVE";

    public const string Message =
        "El rol de caja se asigna solo desde «Usuarios de caja» y no se combina con otros roles del organismo.";

    /// <summary><c>true</c> si alguno de los roles es el de caja del organismo.</summary>
    public static async Task<bool> ContainsCajaRoleAsync(FlitDbContext db, IReadOnlyCollection<Guid> roleIds, CancellationToken ct) =>
        roleIds.Count > 0
        && await db.Roles.AsNoTracking()
            .AnyAsync(r => roleIds.Contains(r.Id) && r.Code == AdminAuthorization.OtCajaRole && r.DeletedAt == null, ct)
            .ConfigureAwait(false);

    /// <summary><c>true</c> si el usuario tiene hoy una asignación activa al rol de caja.</summary>
    public static async Task<bool> UserIsCajaAsync(FlitDbContext db, Guid userId, CancellationToken ct) =>
        await (from a in db.UserRoleAssignments.AsNoTracking()
               join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
               where a.UserId == userId && a.DeletedAt == null && r.Code == AdminAuthorization.OtCajaRole
               select a.Id).AnyAsync(ct).ConfigureAwait(false);
}
