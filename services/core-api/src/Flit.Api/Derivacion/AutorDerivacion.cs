using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Api.Derivacion;

/// <summary>
/// Lo que se sabe del autor en la empresa de la llamada, leído de la base (no de un JWT). <see cref="Permisos"/> son los
/// permisos efectivos: rol + allow − deny (<see cref="EffectivePermissionsCalculator"/>), los mismos que llevaría su token.
/// </summary>
internal sealed record SnapshotAutor(string Estado, bool Miembro, bool Suspendido, bool SuperAdmin, IReadOnlyList<string> Permisos);

/// <summary>Puerto: ¿el usuario puede crear un trámite de esa familia en esa empresa?</summary>
internal interface IAutorDerivacion
{
    Task<bool> PuedeCrearAsync(Guid tenantId, Guid usuarioId, string familia, CancellationToken ct);
}

/// <summary>
/// ADR-0084 §3 regla 2 — regla pura del autor del borrador. Equivale a lo que exige <c>POST /api/v1/tramites/instances</c>
/// (<c>RequireModulePermission("tramites.create")</c> + <c>IProcedureFamilyPermissionGate.CanCreate</c>), más las condiciones
/// que en REST garantiza el login (usuario activo, miembro de la empresa, sin suspensión vigente).
/// </summary>
internal static class ReglaAutorDerivacion
{
    public const string EstadoActivo = "active";

    public const string PermisoCrearTramite = "tramites.create";

    /// <param name="autor">Null si el usuario no existe o está borrado.</param>
    /// <param name="slugFamilia">Crear de la familia del tipo (<see cref="FamilyPermissionSlugs.Create"/>).</param>
    /// <param name="exigenciaActiva"><c>Security:ModulePermissionEnforcement:Enabled</c>: apagado, la ruta REST tampoco exige slugs.</param>
    public static bool Decidir(SnapshotAutor? autor, string slugFamilia, bool exigenciaActiva)
    {
        if (autor is null
            || !string.Equals(autor.Estado, EstadoActivo, StringComparison.Ordinal)
            || !autor.Miembro
            || autor.Suspendido)
            return false;

        if (!exigenciaActiva || autor.SuperAdmin)
            return true;

        return Tiene(autor.Permisos, PermisoCrearTramite) && Tiene(autor.Permisos, slugFamilia);
    }

    private static bool Tiene(IReadOnlyList<string> permisos, string slug) =>
        permisos.Any(p => string.Equals(p, slug, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Calcula los permisos efectivos del autor por id, fuera de una petición HTTP: las mismas lecturas que
/// <c>AuthUserRepository</c> usa para emitir el token (roles activos de la empresa, grants activos, overrides vigentes y
/// productos habilitados) pero acotadas a la empresa de la llamada. Un rol en otra empresa no cuenta: un usuario no puede
/// crear trámites a nombre de una compañía de la que no es miembro. Solo lecturas parametrizadas por EF; sin logs.
/// </summary>
internal sealed class AutorDerivacion(FlitDbContext db, IOptionsMonitor<ModulePermissionEnforcementOptions> exigencia) : IAutorDerivacion
{
    private const string ProductoPlataforma = "plataforma";

    public async Task<bool> PuedeCrearAsync(Guid tenantId, Guid usuarioId, string familia, CancellationToken ct)
    {
        var slugFamilia = FamilyPermissionSlugs.Create(ProcedureFamilyCodes.FromCodeOrOtros(familia));
        var autor = await LeerAsync(tenantId, usuarioId, ct).ConfigureAwait(false);
        return ReglaAutorDerivacion.Decidir(autor, slugFamilia, exigencia.CurrentValue.Enabled);
    }

    private async Task<SnapshotAutor?> LeerAsync(Guid tenantId, Guid usuarioId, CancellationToken ct)
    {
        var estado = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId && u.DeletedAt == null)
            .Select(u => u.Status)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (estado is null)
            return null;

        var roles = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.UserId == usuarioId && a.TenantId == tenantId && a.DeletedAt == null && r.DeletedAt == null && r.IsActive
            select new { r.Id, r.Code }).ToListAsync(ct).ConfigureAwait(false);
        if (roles.Count == 0)
            return new SnapshotAutor(estado, Miembro: false, Suspendido: false, SuperAdmin: false, []);

        var suspendido = await db.UserTempSuspensions.AsNoTracking()
            .Where(UserTempSuspension.VigenteEn(DateTimeOffset.UtcNow))
            .AnyAsync(s => s.UserId == usuarioId && s.TenantId == tenantId, ct).ConfigureAwait(false);
        var superAdmin = roles.Any(r => string.Equals(r.Code, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));
        if (superAdmin)
            return new SnapshotAutor(estado, Miembro: true, suspendido, SuperAdmin: true, []);

        var roleIds = roles.Select(r => r.Id).Distinct().ToList();
        var delRol = await (
            from g in db.RoleGrants.AsNoTracking()
            join p in db.RbacActions.AsNoTracking() on g.PermissionId equals p.Id
            where roleIds.Contains(g.RoleId) && p.IsActive
            select p.Slug).Distinct().ToListAsync(ct).ConfigureAwait(false);

        var lector = new EffectivePermissionOverrideReader(db);
        var overrides = await lector.ListActiveAsync(tenantId, usuarioId, ct).ConfigureAwait(false);
        IReadOnlySet<string> productos = overrides.Any(o => o.Effect == PermissionEffects.Allow && o.ProductCode != ProductoPlataforma)
            ? await lector.EnabledProductsAsync(tenantId, ct).ConfigureAwait(false)
            : new HashSet<string>(StringComparer.Ordinal);
        var efectivos = EffectivePermissionsCalculator.Compute(delRol, overrides, p => p == ProductoPlataforma || productos.Contains(p));

        return new SnapshotAutor(estado, Miembro: true, suspendido, SuperAdmin: false, efectivos);
    }
}
