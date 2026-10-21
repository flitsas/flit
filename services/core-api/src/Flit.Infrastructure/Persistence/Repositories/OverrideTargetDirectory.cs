using Flit.Modules.Security.Domain.PermissionOverrides;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13645 (ADR-0073, FB-9 a FB-16): pertenencia usuario-tenant, roles, estado de invitación y condición de Super Admin.
/// La app conecta como owner de la BD y la RLS no aplica al owner: el aislamiento por tenant sale de los filtros
/// explícitos (<c>TenantId == tenantId</c>) de estas consultas, no de la RLS.
/// <list type="bullet">
/// <item>Pertenencia: asignación de rol vigente en <c>security.user_role_assignments</c> del tenant, o <c>users.home_tenant_id</c> = tenant.</item>
/// <item>Hijas/OT: son tenants propios (<c>identity.tenants</c>); un usuario de una hija u OT solo es miembro de su tenant.</item>
/// <item>Invitación: el usuario nace al aceptar (<c>status = 'active'</c>); pendiente = <c>users.status = 'pending'</c> o invitación <c>pending</c> del mismo correo en el tenant.</item>
/// <item>Super Admin: asignación vigente del rol global <c>SuperAdmin</c> en cualquier tenant.</item>
/// </list>
/// </summary>
internal sealed class OverrideTargetDirectory(FlitDbContext db) : IOverrideTargetDirectory
{
    private const string SuperAdminRole = "SuperAdmin";

    // Mismo criterio que el listado de usuarios (TenantType): el tenant tiene perfil de organismo de tránsito.
    public Task<bool> IsTransitOfficeTenantAsync(Guid tenantId, CancellationToken ct) =>
        db.TransitOfficeProfiles.AsNoTracking().AnyAsync(p => p.TenantId == tenantId, ct);

    public async Task<OverrideTargetInfo> GetAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var tenantExists = await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct).ConfigureAwait(false);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new { u.Email, u.Status, u.HomeTenantId })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is null)
        {
            return new OverrideTargetInfo(tenantExists, false, false, null, string.Empty, false, new HashSet<string>(), false);
        }

        var roleCodes = await TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () => await (
                from a in db.UserRoleAssignments.AsNoTracking()
                join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                where a.TenantId == tenantId && a.UserId == userId && a.DeletedAt == null && r.IsActive && r.DeletedAt == null
                select r.Code).Distinct().ToListAsync(ct).ConfigureAwait(false),
            ct).ConfigureAwait(false);
        var hasAssignment = roleCodes.Count > 0 || await TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () => await db.UserRoleAssignments.AsNoTracking()
                .AnyAsync(a => a.TenantId == tenantId && a.UserId == userId && a.DeletedAt == null, ct).ConfigureAwait(false),
            ct).ConfigureAwait(false);
        var pendingInvitation = await TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () => await db.UserInvitations.AsNoTracking()
                .AnyAsync(i => i.TenantId == tenantId && i.Email == user.Email && i.Status == "pending" && i.DeletedAt == null, ct)
                .ConfigureAwait(false),
            ct).ConfigureAwait(false);

        // Super Admin: el rol global puede estar asignado en cualquier tenant, por eso no se filtra por tenant.
        var isSuperAdmin = await (
            from a in db.UserRoleAssignments.AsNoTracking().IgnoreQueryFilters()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.UserId == userId && a.DeletedAt == null && r.Code == SuperAdminRole && r.IsActive && r.DeletedAt == null
            select a.Id).AnyAsync(ct).ConfigureAwait(false);

        return new OverrideTargetInfo(
            tenantExists, true, hasAssignment || user.HomeTenantId == tenantId, user.HomeTenantId, user.Status,
            pendingInvitation, roleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase), isSuperAdmin);
    }
}
