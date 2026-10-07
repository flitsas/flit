using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Flit.Infrastructure.Security;

/// <summary>
/// HU #13375 (Épica #13216, CF-16, AC2 y AC6) — revalida, antes de procesar cada ítem, que el solicitante del lote
/// conserva acceso al trámite. Lee el estado ACTUAL de la base (no el JWT con el que se creó el lote):
/// <list type="bullet">
///   <item><b>Origen <c>tramites</c></b>: usuario activo y (membresía activa en la compañía congelada del lote
///   — <c>batch.tenant_id</c> — con un rol activo que otorgue el permiso activo <c>consolidado-masivo.download</c>,
///   o rol <c>SuperAdmin</c> activo, el mismo bypass de <c>RequirePermission</c>); el trámite debe ser de esa
///   compañía.</item>
///   <item><b>Origen <c>superadmin</c></b>: usuario activo con rol <c>SuperAdmin</c> activo; la compañía congelada es
///   la del trámite en el ítem.</item>
///   <item>En ambos: el trámite existe, sin borrado lógico, y es de la compañía congelada.</item>
///   <item><b>AC7</b>: el solicitante no tiene una suspensión vigente (<see cref="UserTempSuspension.VigenteEn"/>, la
///   misma regla con la que el login lo bloquea) en la compañía congelada del lote (<c>tramites</c>) o en las compañías
///   donde tiene asignado el rol <c>SuperAdmin</c> activo (<c>superadmin</c>). Si la tiene, el ítem se omite como
///   <c>acceso_revocado</c>; vencida o levantada, se procesa con normalidad.</item>
/// </list>
/// La decisión sobre el solicitante (usuario + rol + suspensión) se cachea 60 s por lote y compañía; la del trámite se
/// consulta en cada ítem. Por esa caché, una suspensión (o su vencimiento) puede tardar hasta 60 s en reflejarse en un
/// lote en curso — aceptado (AC7). Solo lecturas parametrizadas por EF; sin logs.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>var ok = await checker.TieneAccesoAsync(LoteItemContexto.Desde(lote, item), ct);</c>.
/// El origen <c>ot_bandeja</c> (grant vigente al OT) lo resuelve la HU #13392 con su propio procesador.
/// </remarks>
public sealed class ConsolidadoLoteAccessChecker(FlitDbContext db, IMemoryCache cache) : IConsolidadoLoteAccessChecker
{
    /// <summary>Vigencia de la decisión cacheada sobre el solicitante (ADR-0070, CF-16).</summary>
    internal static readonly TimeSpan DuracionCache = TimeSpan.FromSeconds(60);

    private const string UsuarioActivo = "active";

    public async Task<bool> TieneAccesoAsync(LoteItemContexto contexto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        bool solicitanteConAcceso;
        Guid compania;
        switch (contexto.Origen)
        {
            case ConsolidadoExportOrigin.Tramites:
                if (contexto.CompaniaLoteId is not { } companiaLote || contexto.CompaniaTramiteId != companiaLote)
                    return false;
                compania = companiaLote;
                solicitanteConAcceso = await CacheadoAsync(
                    $"consolidado-lote-acceso:{contexto.BatchId:N}:{compania:N}",
                    c => TienePermisoEnCompaniaAsync(contexto.SolicitanteId, compania, c),
                    ct).ConfigureAwait(false);
                break;

            case ConsolidadoExportOrigin.Superadmin:
                compania = contexto.CompaniaCongelada;
                solicitanteConAcceso = await CacheadoAsync(
                    $"consolidado-lote-acceso:{contexto.BatchId:N}:superadmin",
                    c => EsSuperAdminActivoAsync(contexto.SolicitanteId, c),
                    ct).ConfigureAwait(false);
                break;

            default:
                throw new NotSupportedException(
                    $"El origen '{contexto.Origen}' revalida el acceso con su propio procesador de ítem.");
        }

        if (!solicitanteConAcceso)
            return false;

        return await db.ProcedureInstances
            .AsNoTracking()
            .AnyAsync(p => p.Id == contexto.ProcedureInstanceId && p.TenantId == compania && p.DeletedAt == null, ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> CacheadoAsync(string clave, Func<CancellationToken, Task<bool>> calcular, CancellationToken ct)
    {
        if (cache.TryGetValue(clave, out bool valor))
            return valor;

        valor = await calcular(ct).ConfigureAwait(false);
        cache.Set(clave, valor, DuracionCache);
        return valor;
    }

    private Task<bool> UsuarioActivoAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.DeletedAt == null && u.Status == UsuarioActivo, ct);

    /// <summary>Asignaciones activas del usuario cuyo rol sigue activo y sin borrar.</summary>
    private IQueryable<RolActivo> RolesActivos(Guid userId) =>
        from a in db.UserRoleAssignments.AsNoTracking()
        join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
        where a.UserId == userId && a.DeletedAt == null && r.DeletedAt == null && r.IsActive
        select new RolActivo { TenantId = a.TenantId, RoleId = r.Id, Code = r.Code };

    /// <summary>AC7 — suspensiones del usuario vigentes ahora, con la misma regla del login.</summary>
    private IQueryable<UserTempSuspension> SuspensionesVigentes(Guid userId) =>
        db.UserTempSuspensions.AsNoTracking()
            .Where(UserTempSuspension.VigenteEn(DateTimeOffset.UtcNow))
            .Where(s => s.UserId == userId);

    private async Task<bool> EsSuperAdminActivoAsync(Guid userId, CancellationToken ct)
    {
        if (!await UsuarioActivoAsync(userId, ct).ConfigureAwait(false))
            return false;

        var companiasSuperAdmin = RolesActivos(userId).Where(r => r.Code == AdminRoleCodes.SuperAdmin).Select(r => r.TenantId);
        return await companiasSuperAdmin.AnyAsync(ct).ConfigureAwait(false)
            && !await SuspensionesVigentes(userId).AnyAsync(s => companiasSuperAdmin.Contains(s.TenantId), ct).ConfigureAwait(false);
    }

    private async Task<bool> TienePermisoEnCompaniaAsync(Guid userId, Guid compania, CancellationToken ct)
    {
        if (!await UsuarioActivoAsync(userId, ct).ConfigureAwait(false)
            || await SuspensionesVigentes(userId).AnyAsync(s => s.TenantId == compania, ct).ConfigureAwait(false))
            return false;

        var conPermiso = await (
            from r in RolesActivos(userId)
            where r.TenantId == compania
            join g in db.RoleGrants.AsNoTracking() on r.RoleId equals g.RoleId
            join p in db.RbacActions.AsNoTracking() on g.PermissionId equals p.Id
            where p.Slug == ConsolidadoLotePermisos.Descargar && p.IsActive && p.DeletedAt == null
            select g.Id).AnyAsync(ct).ConfigureAwait(false);

        return conPermiso
            || await RolesActivos(userId).AnyAsync(r => r.Code == AdminRoleCodes.SuperAdmin, ct).ConfigureAwait(false);
    }

    /// <summary>Proyección con inicializador (no constructor): EF traduce los filtros posteriores sobre sus miembros.</summary>
    private sealed class RolActivo
    {
        public Guid TenantId { get; init; }

        public Guid RoleId { get; init; }

        public string Code { get; init; } = string.Empty;
    }
}
