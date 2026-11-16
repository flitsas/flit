using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Usuarios;

/// <summary>
/// Lo que se sabe del actor, leído de la base (no de un JWT). <see cref="Permisos"/> son los permisos efectivos en la
/// empresa objetivo (rol + allow − deny), los mismos que llevaría su token.
/// </summary>
/// <param name="SuperAdmin">Tiene SuperAdmin vigente (en su empresa, la interna de FLIT) y no está suspendido en ella.</param>
/// <param name="AdminProducto">Tiene vigente el rol admin del producto en la empresa objetivo.</param>
/// <param name="Suspendido">Suspensión temporal vigente en la empresa objetivo.</param>
internal sealed record SnapshotActor(string Estado, bool SuperAdmin, bool AdminProducto, bool Suspendido, IReadOnlyList<string> Permisos);

/// <summary>Puerto: ¿el usuario puede gestionar los usuarios del producto en esa empresa (o globalmente)?</summary>
internal interface IActorUsuariosProducto
{
    /// <param name="tenantId"><c>null</c> en la operación global (conteos): solo el SuperAdmin.</param>
    /// <returns>El actor reverificado, o <c>null</c> si no puede.</returns>
    Task<ProductActor?> VerificarAsync(Guid? tenantId, Guid actorId, string producto, CancellationToken ct);
}

/// <summary>
/// ADR-0089 regla 2 (contrato §3.4) — regla pura del actor: cuenta activa y, o SuperAdmin, o en la empresa objetivo admin
/// del producto vigente con el permiso <c>&lt;producto&gt;.usuarios.gestionar</c> y sin suspensión. El permiso se exige
/// siempre (no depende de <c>Security:ModulePermissionEnforcement</c>): esta es la única barrera de la plataforma.
/// </summary>
internal static class ReglaActorUsuariosProducto
{
    public const string EstadoActivo = "active";

    public static ProductActor? Decidir(Guid actorId, SnapshotActor? actor, string producto, bool global)
    {
        if (actor is null || !string.Equals(actor.Estado, EstadoActivo, StringComparison.Ordinal))
            return null;
        if (actor.SuperAdmin)
            return new ProductActor(actorId, IsSuperAdmin: true);
        if (global || actor.Suspendido || !actor.AdminProducto)
            return null;

        var permiso = ProductRoleRules.ManagePermission(producto);
        return actor.Permisos.Any(p => string.Equals(p, permiso, StringComparison.OrdinalIgnoreCase))
            ? new ProductActor(actorId, IsSuperAdmin: false)
            : null;
    }
}

/// <summary>
/// Lee el actor por id (patrón <c>AutorDerivacion</c>): estado de la cuenta, SuperAdmin en cualquier empresa (el suyo es el
/// tenant interno de FLIT, nunca el objetivo), y en la empresa objetivo el rol admin del producto, la suspensión temporal y
/// los permisos efectivos. Solo lecturas parametrizadas por EF; sin logs.
/// </summary>
internal sealed class ActorUsuariosProducto(FlitDbContext db) : IActorUsuariosProducto
{
    private const string SuperAdminRole = "SuperAdmin";
    private const string ProductoPlataforma = "plataforma";

    public async Task<ProductActor?> VerificarAsync(Guid? tenantId, Guid actorId, string producto, CancellationToken ct)
    {
        var actor = await LeerAsync(tenantId, actorId, producto, ct).ConfigureAwait(false);
        return ReglaActorUsuariosProducto.Decidir(actorId, actor, producto, global: tenantId is null);
    }

    private async Task<SnapshotActor?> LeerAsync(Guid? tenantId, Guid actorId, string producto, CancellationToken ct)
    {
        var estado = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId && u.DeletedAt == null)
            .Select(u => u.Status)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (estado is null)
            return null;

        var ahora = DateTimeOffset.UtcNow;
        var roles = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.UserId == actorId && a.DeletedAt == null && r.DeletedAt == null && r.IsActive
            select new { a.TenantId, r.Id, r.Code }).ToListAsync(ct).ConfigureAwait(false);
        var suspensiones = await db.UserTempSuspensions.AsNoTracking()
            .Where(UserTempSuspension.VigenteEn(ahora))
            .Where(s => s.UserId == actorId)
            .Select(s => s.TenantId).ToListAsync(ct).ConfigureAwait(false);

        var superAdmin = roles.Any(r => string.Equals(r.Code, SuperAdminRole, StringComparison.OrdinalIgnoreCase) && !suspensiones.Contains(r.TenantId));
        if (superAdmin || tenantId is not { } empresa)
            return new SnapshotActor(estado, superAdmin, AdminProducto: false, Suspendido: false, []);

        var enEmpresa = roles.Where(r => r.TenantId == empresa).ToList();
        var adminProducto = enEmpresa.Any(r => string.Equals(r.Code, ProductRoleRules.AdminRole(producto), StringComparison.Ordinal));
        if (!adminProducto)
            return new SnapshotActor(estado, SuperAdmin: false, AdminProducto: false, suspensiones.Contains(empresa), []);

        var roleIds = enEmpresa.Select(r => r.Id).Distinct().ToList();
        var delRol = await (
            from g in db.RoleGrants.AsNoTracking()
            join p in db.RbacActions.AsNoTracking() on g.PermissionId equals p.Id
            where roleIds.Contains(g.RoleId) && p.IsActive
            select p.Slug).Distinct().ToListAsync(ct).ConfigureAwait(false);

        var lector = new EffectivePermissionOverrideReader(db);
        var overrides = await lector.ListActiveAsync(empresa, actorId, ct).ConfigureAwait(false);
        IReadOnlySet<string> productos = overrides.Any(o => o.Effect == PermissionEffects.Allow && o.ProductCode != ProductoPlataforma)
            ? await lector.EnabledProductsAsync(empresa, ct).ConfigureAwait(false)
            : new HashSet<string>(StringComparer.Ordinal);
        var efectivos = EffectivePermissionsCalculator.Compute(delRol, overrides, p => p == ProductoPlataforma || productos.Contains(p));

        return new SnapshotActor(estado, SuperAdmin: false, AdminProducto: true, suspensiones.Contains(empresa), efectivos);
    }
}
