using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Application.Products;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>Quién es un usuario de caja y de qué organismo (RPC <c>ResolverUsuarioCaja</c>). <c>Activo = false</c> no trae datos.</summary>
public sealed record UsuarioCajaResuelto(bool Activo, Guid OrganismoId, string OrganismoCodigo, string OrganismoNombre, string UsuarioNombre)
{
    public static UsuarioCajaResuelto Inactivo { get; } = new(false, Guid.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// Una fila de «Usuarios de caja» del organismo. <c>Status</c>: <c>invited</c> (invitación pendiente: trae <c>InvitationId</c> y no
/// <c>UserId</c>), <c>active</c> o <c>suspended</c>.
/// </summary>
public sealed record CajaUsuarioFila(Guid? UserId, Guid? InvitationId, string FullName, string Email, string Status, DateTimeOffset? CreatedAt);

/// <summary>
/// HU #14454 / #14455 (Épica #14210, ADR-0082) — lecturas del rol <c>ot_caja</c> sobre la base de seguridad. Sin estado ni caché: un
/// usuario desactivado pierde el acceso en su siguiente request.
/// </summary>
public static class CajaUsuarios
{
    /// <summary>
    /// <c>Activo = true</c> solo si el usuario existe, es de esa empresa, está <c>active</c>, sin suspensión vigente, con asignación
    /// vigente al rol <c>ot_caja</c>, la empresa es un organismo de tránsito con el producto <c>caja</c> encendido y el organismo está
    /// activo. Cualquier otro caso es <see cref="UsuarioCajaResuelto.Inactivo"/> (sin distinguir la causa hacia fuera).
    /// </summary>
    public static async Task<UsuarioCajaResuelto> ResolverAsync(
        FlitDbContext db, IProductAccessResolver acceso, Guid tenantId, Guid userId, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(acceso);
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            return UsuarioCajaResuelto.Inactivo;

        var usuario = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null && u.Status == "active" && u.HomeTenantId == tenantId)
            .Select(u => new { u.DisplayName })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (usuario is null)
            return UsuarioCajaResuelto.Inactivo;

        if (await db.UserTempSuspensions.AsNoTracking().Where(UserTempSuspension.VigenteEn(ahora))
                .AnyAsync(s => s.UserId == userId && s.TenantId == tenantId, ct).ConfigureAwait(false))
            return UsuarioCajaResuelto.Inactivo;

        var esCaja = await (from a in db.UserRoleAssignments.AsNoTracking()
                            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                            where a.UserId == userId && a.TenantId == tenantId && a.DeletedAt == null
                                  && r.Code == CajaOnly.RoleCode && r.IsActive && r.DeletedAt == null
                            select a.Id).AnyAsync(ct).ConfigureAwait(false);
        if (!esCaja)
            return UsuarioCajaResuelto.Inactivo;

        var organismo = await (from p in db.TransitOfficeProfiles.AsNoTracking()
                               join o in db.TransitOffices.AsNoTracking() on p.TransitOfficeId equals o.Id
                               where p.TenantId == tenantId && o.IsActive
                               select new { o.Id, o.Code, o.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (organismo is null)
            return UsuarioCajaResuelto.Inactivo;

        var producto = await acceso.ResolveAsync(userId, tenantId, ProductCodes.Caja, ct).ConfigureAwait(false);
        return producto.ProductEnabled
            ? new UsuarioCajaResuelto(true, organismo.Id, organismo.Code, organismo.Name, usuario.DisplayName)
            : UsuarioCajaResuelto.Inactivo;
    }

    /// <summary>Usuarios con rol <c>ot_caja</c> (activos y suspendidos) e invitaciones pendientes con ese rol, del tenant del organismo.</summary>
    public static async Task<IReadOnlyList<CajaUsuarioFila>> ListarAsync(FlitDbContext db, Guid tenantId, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var usuarios = await (from a in db.UserRoleAssignments.AsNoTracking()
                              join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                              join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                              where a.TenantId == tenantId && a.DeletedAt == null && u.DeletedAt == null && r.Code == CajaOnly.RoleCode
                              select new
                              {
                                  u.Id,
                                  u.DisplayName,
                                  u.Email,
                                  u.Status,
                                  u.CreatedAt,
                                  Suspendido = db.UserTempSuspensions.Where(UserTempSuspension.VigenteEn(ahora)).Any(s => s.UserId == u.Id && s.TenantId == tenantId),
                              }).ToListAsync(ct).ConfigureAwait(false);

        var invitaciones = await (from i in db.UserInvitations.AsNoTracking()
                                  join r in db.Roles.AsNoTracking() on i.RoleId equals r.Id
                                  where i.TenantId == tenantId && i.DeletedAt == null && i.Status == "pending" && r.Code == CajaOnly.RoleCode
                                  select new { i.Id, i.FullName, i.Email, i.CreatedAt }).ToListAsync(ct).ConfigureAwait(false);

        return
        [
            .. invitaciones.Select(i => new CajaUsuarioFila(null, i.Id, i.FullName, i.Email, "invited", i.CreatedAt)),
            .. usuarios.Select(u => new CajaUsuarioFila(u.Id, null, u.DisplayName, u.Email, u.Suspendido || u.Status != "active" ? "suspended" : "active", u.CreatedAt)),
        ];
    }

    /// <summary><c>true</c> si el usuario tiene asignación activa a <c>ot_caja</c> en ese tenant (alcance de suspender y reactivar).</summary>
    public static Task<bool> EsDeCajaAsync(FlitDbContext db, Guid tenantId, Guid userId, CancellationToken ct) =>
        (from a in db.UserRoleAssignments.AsNoTracking()
         join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
         where a.UserId == userId && a.TenantId == tenantId && a.DeletedAt == null && r.Code == CajaOnly.RoleCode
         select a.Id).AnyAsync(ct);

    /// <summary><c>true</c> si la invitación es de ese tenant y del rol de caja (alcance de reenviar).</summary>
    public static Task<bool> InvitacionEsDeCajaAsync(FlitDbContext db, Guid tenantId, Guid invitationId, CancellationToken ct) =>
        (from i in db.UserInvitations.AsNoTracking()
         join r in db.Roles.AsNoTracking() on i.RoleId equals r.Id
         where i.Id == invitationId && i.TenantId == tenantId && i.DeletedAt == null && r.Code == CajaOnly.RoleCode
         select i.Id).AnyAsync(ct);

    /// <summary>Id del rol global <c>ot_caja</c> de organismos de tránsito, o <c>null</c> si el DDL 199 no corrió.</summary>
    public static Task<Guid?> RolCajaIdAsync(FlitDbContext db, CancellationToken ct) =>
        db.Roles.AsNoTracking()
            .Where(r => r.Code == CajaOnly.RoleCode && r.TargetEntityType == "TRANSIT_OFFICE" && r.IsActive && r.DeletedAt == null)
            .Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
}
