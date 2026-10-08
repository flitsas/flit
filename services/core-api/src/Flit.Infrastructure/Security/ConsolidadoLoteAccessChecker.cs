using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
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
///   SIEMPRE la del trámite en el ítem (<c>items.tenant_id</c>), nunca la del lote ni el <c>scope_tenant_id</c>
///   (HU #13384 AC1).</item>
///   <item><b>Origen <c>ot_bandeja</c></b> (HU #13392, Q-ADR1): lote creado por el Super Admin desde la bandeja
///   (<c>requested_role_code = SuperAdmin</c>) ⇒ solo su rol <c>SuperAdmin</c> activo; si no, la misma regla de
///   <c>tramites</c> sobre el tenant OT del lote (<c>batch.tenant_id</c>): membresía activa con un rol que otorgue el
///   permiso. Aquí NO se mira el trámite: es de la compañía cliente y lo revalida el procesador OT con la regla de la
///   bandeja (<c>OtConsolidadoLoteEntregador</c>).</item>
///   <item>En <c>tramites</c> y <c>superadmin</c>: el trámite existe, sin borrado lógico, y es de la compañía congelada.</item>
///   <item><b>AC7</b>: el solicitante no tiene una suspensión vigente (<see cref="UserTempSuspension.VigenteEn"/>, la
///   misma regla con la que el login lo bloquea) en la compañía congelada del lote (<c>tramites</c> y el tenant OT en
///   <c>ot_bandeja</c>) o en las compañías donde tiene asignado el rol <c>SuperAdmin</c> activo (<c>superadmin</c> y el
///   lote OT del Super Admin). Si la tiene, el ítem se omite como
///   <c>acceso_revocado</c>; vencida o levantada, se procesa con normalidad.</item>
///   <item><b>Lote de red</b> (HU #13418, <c>network_scope</c>, adenda v7 A7.4): la cabeza P es la compañía del lote.
///   Lote acotado (<c>scope_tenant_id</c>) ⇒ el ítem debe ser de esa hija. Ítem de P ⇒ la regla de <c>tramites</c>.
///   Ítem de una hija ⇒ además: rol <c>AdminCompany</c> activo en P leído de la BD (no del JWT), <c>group_read_scope</c>
///   encendido, P sigue siendo cabeza con tipo de cabeza válido, interruptor <c>network_documents_concesion</c> si P es
///   CONCESIÓN (cacheado 60 s por lote) y la compañía del ítem sigue teniendo <c>parent_tenant_id = P</c> (por ítem). La
///   jerarquía y los interruptores se leen DIRECTAMENTE: <c>DbTenantScopeResolver</c> y <c>DbHierarchySwitches</c> se
///   tragan las excepciones (fail-closed) y un error de BD acabaría como «acceso revocado»; aquí se propaga y el
///   procesamiento del ítem lo reprograma como fallo técnico (AC8). Una excepción no se cachea.</item>
/// </list>
/// La decisión sobre el solicitante (usuario + rol + suspensión) se cachea 60 s por lote y compañía; la del trámite se
/// consulta en cada ítem. Por esa caché, una suspensión (o su vencimiento) puede tardar hasta 60 s en reflejarse en un
/// lote en curso — aceptado (AC7). Solo lecturas parametrizadas por EF; sin logs.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>var ok = await checker.TieneAccesoAsync(LoteItemContexto.Desde(lote, item), ct);</c>.
/// En <c>ot_bandeja</c> el trámite lo revalida la HU #13392 con la regla de la bandeja (el grant no cuenta, D-FB1).
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
            case ConsolidadoExportOrigin.Tramites when contexto.RedActiva:
                // HU #13418 — lote de la vista de red de la cabeza (CompaniaLoteId).
                if (contexto.CompaniaLoteId is not { } cabeza
                    || (contexto.ScopeTenantId is { } hijaAcotada && contexto.CompaniaTramiteId != hijaAcotada))
                    return false;
                compania = contexto.CompaniaTramiteId;
                solicitanteConAcceso = await CacheadoAsync(
                        $"consolidado-lote-acceso:{contexto.BatchId:N}:{cabeza:N}",
                        c => TienePermisoEnCompaniaAsync(contexto.SolicitanteId, cabeza, c),
                        ct).ConfigureAwait(false)
                    && (compania == cabeza || await RedVigenteParaAsync(contexto, cabeza, ct).ConfigureAwait(false));
                break;

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
                // HU #13384 AC1: siempre la compañía del ítem (items.tenant_id), nunca la del lote ni el scope.
                compania = contexto.CompaniaTramiteId;
                solicitanteConAcceso = await CacheadoAsync(
                    $"consolidado-lote-acceso:{contexto.BatchId:N}:superadmin",
                    c => EsSuperAdminActivoAsync(contexto.SolicitanteId, c),
                    ct).ConfigureAwait(false);
                break;

            case ConsolidadoExportOrigin.OtBandeja:
                // HU #13392 — solo el solicitante: si el trámite sigue en la bandeja lo decide el procesador OT
                // (OtConsolidadoLoteEntregador, con la regla de la bandeja), porque el trámite es de la compañía cliente
                // y no del tenant OT del lote.
                if (contexto.CompaniaLoteId is not { } tenantOt)
                    return false;
                var esSuperAdmin = string.Equals(contexto.RolSolicitante, AdminRoleCodes.SuperAdmin, StringComparison.Ordinal);
                return await CacheadoAsync(
                    $"consolidado-lote-acceso:{contexto.BatchId:N}:ot_bandeja",
                    c => esSuperAdmin
                        ? EsSuperAdminActivoAsync(contexto.SolicitanteId, c)
                        : TienePermisoEnCompaniaAsync(contexto.SolicitanteId, tenantOt, c),
                    ct).ConfigureAwait(false);

            default:
                throw new NotSupportedException($"El origen '{contexto.Origen}' no tiene revalidación de acceso.");
        }

        if (!solicitanteConAcceso)
            return false;

        return await db.ProcedureInstances
            .AsNoTracking()
            .AnyAsync(p => p.Id == contexto.ProcedureInstanceId && p.TenantId == compania && p.DeletedAt == null, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// HU #13418 — ¿el ítem de la hija <see cref="LoteItemContexto.CompaniaTramiteId"/> sigue al alcance de la red de
    /// <paramref name="cabeza"/>? Solicitante + red: cacheado 60 s por lote (AC5, AC6); jerarquía: por ítem (AC4).
    /// </summary>
    private async Task<bool> RedVigenteParaAsync(LoteItemContexto contexto, Guid cabeza, CancellationToken ct)
    {
        var redVigente = await CacheadoAsync(
            $"consolidado-lote-acceso:{contexto.BatchId:N}:red",
            c => RolYRedVigentesAsync(contexto.SolicitanteId, cabeza, contexto.CompaniaTramiteId, c),
            ct).ConfigureAwait(false);
        if (!redVigente)
            return false;

        var hija = contexto.CompaniaTramiteId;
        return await db.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Id == hija && t.ParentTenantId == cabeza, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Rol <c>AdminCompany</c> activo del solicitante en la cabeza (BD) y red encendida: <c>group_read_scope</c>, la cabeza
    /// sigue marcada como tal con un tipo de cabeza válido y la clase puede leer documentos de su red según
    /// <see cref="NetworkDocumentsPolicy.ValidateKind"/> (CONCESIÓN solo con <c>network_documents_concesion</c>, P2 = a).
    /// Una fila de interruptor ausente es «apagado» (como en <c>DbHierarchySwitches</c>); un error de BD se propaga.
    /// <paramref name="hija"/> solo sirve para armar el alcance de grupo (no cambia la decisión, cacheada por lote).
    /// </summary>
    private async Task<bool> RolYRedVigentesAsync(Guid userId, Guid cabeza, Guid hija, CancellationToken ct)
    {
        var esAdminDeLaCabeza = await RolesActivos(userId)
            .AnyAsync(r => r.TenantId == cabeza && r.Code == NetworkScopePolicy.HeadAdminRole, ct)
            .ConfigureAwait(false);
        if (!esAdminDeLaCabeza || !await InterruptorAsync(HierarchySwitch.GroupReadScopeKey, ct).ConfigureAwait(false))
            return false;

        var tipoCabeza = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == cabeza && t.IsGroupParent)
            .Select(t => t.TenantType)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (!GroupKindCodes.TryParse(tipoCabeza, out var clase))
            return false;

        // Code review Obs1 — regla única de documentos de red: la decide NetworkDocumentsPolicy (fail-closed ante una
        // clase nueva), no una copia de su condición. El interruptor se sigue leyendo aquí directamente, no con
        // DbHierarchySwitches (que se traga los errores), para que un error de BD se propague (AC8). El grupo lleva a la
        // hija: sin hijos TenantScope.Group degrada a Single, sin clase, y la política lo trataría como «sin documentos».
        var documentosConcesion = await InterruptorAsync(HierarchySwitch.NetworkDocumentsConcesionKey, ct).ConfigureAwait(false);
        return NetworkDocumentsPolicy.ValidateKind(TenantScope.Group(cabeza, [hija], clase), documentosConcesion) is null;
    }

    private async Task<bool> InterruptorAsync(string clave, CancellationToken ct) =>
        await db.HierarchySwitches
            .AsNoTracking()
            .Where(s => s.SwitchKey == clave)
            .Select(s => (bool?)s.IsEnabled)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? false;

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
