using Flit.Modules.Security.Domain.PermissionOverrides;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13647: lectura de <c>security.user_permission_audit</c> con nombres de actor, usuario, tenant, permiso y módulo resueltos por
/// LEFT JOIN en una sola consulta. Filtros explícitos por tenant (la app conecta como owner y la RLS no aplica);
/// el orden created_at DESC usa <c>ix_user_permission_audit_tenant_target</c> / <c>_tenant_actor</c>.
/// </summary>
internal sealed class PermissionAuditReader(FlitDbContext db) : IPermissionAuditReader
{
    public async Task<PermissionAuditPage> QueryAsync(PermissionAuditFilter filter, CancellationToken ct)
    {
        var audit = db.UserPermissionAudits.AsNoTracking().AsQueryable();
        if (filter.TenantId is { } tenant)
        {
            audit = audit.Where(a => a.TenantId == tenant);
        }

        if (filter.UserId is { } user)
        {
            audit = audit.Where(a => a.TargetUserId == user);
        }

        if (filter.ActorId is { } actor)
        {
            audit = audit.Where(a => a.ActorUserId == actor);
        }

        if (filter.From is { } from)
        {
            audit = audit.Where(a => a.CreatedAt >= from);
        }

        if (filter.To is { } to)
        {
            audit = audit.Where(a => a.CreatedAt <= to);
        }

        var total = await audit.CountAsync(ct).ConfigureAwait(false);
        var users = db.Users.AsNoTracking().IgnoreQueryFilters();
        var rows = await (
            from a in audit
            join t in db.Tenants.AsNoTracking() on a.TenantId equals t.Id into tj
            from t in tj.DefaultIfEmpty()
            join ua in users on a.ActorUserId equals ua.Id into uaj
            from ua in uaj.DefaultIfEmpty()
            join ut in users on a.TargetUserId equals ut.Id into utj
            from ut in utj.DefaultIfEmpty()
            orderby a.CreatedAt descending, a.Id descending
            select new PermissionAuditRecord(
                a.Id, a.TenantId, t.LegalName, a.ActorUserId, ua.DisplayName, ua.Email, a.TargetUserId, ut.DisplayName, ut.Email,
                a.ProductCode, a.ModuleCode, a.PermissionSlug, a.Action, a.Outcome, a.ReasonCode, a.ValueBefore, a.ValueAfter,
                a.BatchId, a.CreatedAt,
                db.RbacActions.Where(p => p.Slug == a.PermissionSlug).Select(p => p.Name).FirstOrDefault(),
                db.SecurityModules.Where(m => m.Code == a.ModuleCode).Select(m => m.Name).FirstOrDefault()))
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return new PermissionAuditPage(rows, total, filter.Page, filter.PageSize);
    }

    public Task<IReadOnlyList<OverrideGrantInfo>> GetGrantInfoAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () => (IReadOnlyList<OverrideGrantInfo>)await (
                from o in db.UserPermissionOverrides.AsNoTracking()
                where o.TenantId == tenantId && o.UserId == userId && o.DeletedAt == null
                join g in db.Users.AsNoTracking().IgnoreQueryFilters() on o.GrantedBy equals g.Id into gj
                from g in gj.DefaultIfEmpty()
                select new OverrideGrantInfo(o.PermissionId, g.DisplayName, o.CreatedAt)).ToListAsync(ct).ConfigureAwait(false),
            ct);
}
