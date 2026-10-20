using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Identity.Api.Grpc;

/// <summary>
/// Lecturas de usuarios de una empresa para el gRPC de Identidad (HU #13334), en lectura y sin tracking. Un usuario es
/// de la empresa si tiene al menos una asignación de rol vigente en ella: asignación sin borrar y rol activo sin borrar
/// (la misma regla de <c>ProductAccessStore.GetUserGrantsAsync</c>). <c>home_tenant_id</c> no cuenta: es opcional y la
/// empresa real sale de la asignación.
/// </summary>
internal sealed class UsuariosEmpresaQuery(IIdentityDb db)
{
    internal sealed record UsuarioFila(Guid Id, string Email, string Nombre, string Estado);

    internal sealed record RolFila(Guid UsuarioId, Guid Id, string Codigo, string Producto);

    public const string EstadoActivo = "active";

    /// <summary>Una página de usuarios, ordenados por correo e id (orden estable entre páginas).</summary>
    public async Task<IReadOnlyList<UsuarioFila>> ListarAsync(
        Guid tenantId, string? producto, bool incluirNoActivos, int saltar, int tomar, CancellationToken ct)
    {
        var usuarios = db.Users.AsNoTracking()
            .Where(u => u.DeletedAt == null && RolesVigentes(tenantId, producto).Any(a => a.UserId == u.Id));
        if (!incluirNoActivos)
            usuarios = usuarios.Where(u => u.Status == EstadoActivo);

        return await usuarios
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Skip(saltar).Take(tomar)
            .Select(u => new UsuarioFila(u.Id, u.Email, u.DisplayName, u.Status))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>El usuario, solo si es de la empresa (con cualquier rol vigente en ella, de cualquier producto).</summary>
    public async Task<UsuarioFila?> ObtenerAsync(Guid tenantId, Guid usuarioId, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId && u.DeletedAt == null && RolesVigentes(tenantId, null).Any(a => a.UserId == u.Id))
            .Select(u => new UsuarioFila(u.Id, u.Email, u.DisplayName, u.Status))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    /// <summary>Roles vigentes en la empresa de estos usuarios, filtrados por producto si se indica.</summary>
    public async Task<ILookup<Guid, RolFila>> RolesAsync(Guid tenantId, IReadOnlyCollection<Guid> usuarioIds, string? producto, CancellationToken ct)
    {
        // Distinct: una asignación repetida del mismo rol no duplica el rol en la respuesta.
        var filas = await (
            from a in RolesVigentes(tenantId, producto)
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where usuarioIds.Contains(a.UserId)
            select new { a.UserId, r.Id, r.Code, r.ProductCode }
        ).Distinct().ToListAsync(ct).ConfigureAwait(false);

        return filas
            .Select(f => new RolFila(f.UserId, f.Id, f.Code, f.ProductCode))
            .OrderBy(f => f.Producto, StringComparer.Ordinal).ThenBy(f => f.Codigo, StringComparer.Ordinal)
            .ToLookup(f => f.UsuarioId);
    }

    private IQueryable<Flit.Infrastructure.Persistence.Entities.Security.UserRoleAssignment> RolesVigentes(Guid tenantId, string? producto) =>
        db.UserRoleAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.DeletedAt == null
                && db.Roles.Any(r => r.Id == a.RoleId && r.DeletedAt == null && r.IsActive
                    && (producto == null || r.ProductCode == producto)));
}
