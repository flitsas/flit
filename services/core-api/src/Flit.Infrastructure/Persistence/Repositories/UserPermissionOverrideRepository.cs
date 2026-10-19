using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Permisos individuales por usuario (HU #13642, ADR-0071). Escribe overrides y bitácora en la misma transacción
/// bajo el contexto RLS del tenant; la unicidad la garantiza el índice parcial <c>uq_user_permission_overrides_active</c>.
/// </summary>
internal sealed class UserPermissionOverrideRepository(FlitDbContext db) : IUserPermissionOverrideRepository
{
    private const string UniqueIndex = "uq_user_permission_overrides_active";

    public async Task<ModuleActionsSnapshot?> GetModuleActionsAsync(string moduleCode, CancellationToken ct)
    {
        var module = await db.SecurityModules.AsNoTracking()
            .Where(m => m.Code == moduleCode && m.IsActive && m.DeletedAt == null)
            .Select(m => new { m.Id, m.Code, m.ProductCode })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (module is null)
        {
            return null;
        }

        var actions = await db.RbacActions.AsNoTracking()
            .Where(a => a.ModuleId == module.Id && a.IsActive && a.DeletedAt == null)
            .OrderBy(a => a.Slug)
            .Select(a => new ModuleActionRef(a.Id, a.Slug))
            .ToListAsync(ct).ConfigureAwait(false);
        return new ModuleActionsSnapshot(module.Code, module.ProductCode, actions);
    }

    public Task<IReadOnlyList<UserPermissionOverride>> ListActiveAsync(
        Guid tenantId, Guid userId, string productCode, CancellationToken ct) =>
        TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () => (IReadOnlyList<UserPermissionOverride>)await db.UserPermissionOverrides.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.UserId == userId && o.ProductCode == productCode && o.DeletedAt == null)
                .Select(o => new UserPermissionOverride(
                    o.Id, o.TenantId, o.UserId, o.ProductCode, o.PermissionId, o.Effect, o.Source, o.BatchId, o.GrantedBy))
                .ToListAsync(ct).ConfigureAwait(false),
            ct);

    public Task AddAsync(
        Guid tenantId,
        IReadOnlyList<UserPermissionOverride> items,
        IReadOnlyList<UserPermissionAuditEntry> audit,
        CancellationToken ct) =>
        TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () =>
            {
                await AddCoreAsync(items, audit, ct).ConfigureAwait(false);
                return true;
            },
            ct);

    private async Task AddCoreAsync(
        IReadOnlyList<UserPermissionOverride> items,
        IReadOnlyList<UserPermissionAuditEntry> audit,
        CancellationToken ct)
    {
        db.UserPermissionOverrides.AddRange(items.Select(o => new UserPermissionOverrideEntity
        {
            Id = o.Id,
            TenantId = o.TenantId,
            UserId = o.UserId,
            ProductCode = o.ProductCode,
            PermissionId = o.PermissionId,
            Effect = o.Effect,
            Source = o.Source,
            BatchId = o.BatchId,
            GrantedBy = o.GrantedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = o.GrantedBy,
        }));
        db.UserPermissionAudits.AddRange(audit.Select(a => new UserPermissionAuditEntity
        {
            Id = Guid.NewGuid(),
            TenantId = a.TenantId,
            ActorUserId = a.ActorUserId,
            TargetUserId = a.TargetUserId,
            ProductCode = a.ProductCode,
            ModuleCode = a.ModuleCode,
            PermissionSlug = a.PermissionSlug,
            Action = a.Action,
            Outcome = a.Outcome,
            ReasonCode = a.ReasonCode,
            ValueBefore = a.ValueBefore,
            ValueAfter = a.ValueAfter,
            BatchId = a.BatchId,
            CreatedAt = DateTimeOffset.UtcNow,
        }));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueIndex })
        {
            throw new DuplicateUserPermissionOverrideException();
        }
    }

    // HU #13644 ---------------------------------------------------------------------------------

    private sealed class CatalogRow
    {
        public Guid Id { get; set; }

        public string Slug { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string ModuleCode { get; set; } = string.Empty;

        public string ProductCode { get; set; } = string.Empty;

        public string Enforced { get; set; } = "false";
    }

    private IQueryable<CatalogRow> Catalog() => db.Database.SqlQuery<CatalogRow>($"""
        SELECT p.id AS id, p.slug AS slug, p.name AS name, m.code AS module_code,
               m.product_code AS product_code, p.enforced AS enforced
          FROM security.permissions p
          JOIN security.modules m ON m.id = p.module_id
         WHERE p.is_active AND p.deleted_at IS NULL AND m.deleted_at IS NULL
        """);

    private static PermissionCatalogEntry ToEntry(CatalogRow r) =>
        new(r.Id, r.Slug, r.Name, r.ModuleCode, r.ProductCode, r.Enforced);

    public async Task<PermissionCatalogEntry?> GetPermissionAsync(Guid permissionId, CancellationToken ct)
    {
        var row = await Catalog().Where(r => r.Id == permissionId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return row is null ? null : ToEntry(row);
    }

    private static readonly string[] AdministratorRoleCodes = ["AdminCompany", "ot_admin", "admin_tramites"];

    public Task<IReadOnlyList<Guid>> ListTenantAdministratorIdsAsync(Guid tenantId, CancellationToken ct) =>
        TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () => (IReadOnlyList<Guid>)await (
                from a in db.UserRoleAssignments.AsNoTracking()
                join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                where a.TenantId == tenantId && a.DeletedAt == null && r.IsActive && r.DeletedAt == null
                      && AdministratorRoleCodes.Contains(r.Code)
                select a.UserId).Distinct().ToListAsync(ct).ConfigureAwait(false),
            ct);

    public async Task<IReadOnlyList<PermissionCatalogEntry>> ListEnforcedPermissionsAsync(CancellationToken ct) =>
        (await Catalog().Where(r => r.Enforced == "true").OrderBy(r => r.Slug).ToListAsync(ct).ConfigureAwait(false))
        .Select(ToEntry).ToList();

    public async Task<IReadOnlySet<Guid>> ListExcludedPermissionIdsAsync(CancellationToken ct) =>
        (await Catalog().Where(r => r.Enforced == "excluded").Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();

    public Task<IReadOnlySet<Guid>> GetRolePermissionIdsAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () =>
            {
                var ids = await (
                    from a in db.UserRoleAssignments.AsNoTracking()
                    join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                    join g in db.RoleGrants.AsNoTracking() on r.Id equals g.RoleId
                    join p in db.RbacActions.AsNoTracking() on g.PermissionId equals p.Id
                    where a.TenantId == tenantId && a.UserId == userId && a.DeletedAt == null
                          && r.IsActive && r.DeletedAt == null && p.IsActive && p.DeletedAt == null
                    select g.PermissionId).Distinct().ToListAsync(ct).ConfigureAwait(false);
                return (IReadOnlySet<Guid>)ids.ToHashSet();
            },
            ct);

    public Task<IReadOnlyList<UserPermissionOverride>> ListActiveAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () => (IReadOnlyList<UserPermissionOverride>)await db.UserPermissionOverrides.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.UserId == userId && o.DeletedAt == null)
                .Select(o => new UserPermissionOverride(
                    o.Id, o.TenantId, o.UserId, o.ProductCode, o.PermissionId, o.Effect, o.Source, o.BatchId, o.GrantedBy))
                .ToListAsync(ct).ConfigureAwait(false),
            ct);

    public Task ApplyAsync(
        Guid tenantId,
        Guid actorUserId,
        IReadOnlyList<Guid> removeOverrideIds,
        IReadOnlyList<UserPermissionOverride> add,
        IReadOnlyList<UserPermissionAuditEntry> audit,
        CancellationToken ct) =>
        TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () =>
            {
                if (removeOverrideIds.Count > 0)
                {
                    var now = DateTimeOffset.UtcNow;
                    var rows = await db.UserPermissionOverrides
                        .Where(o => removeOverrideIds.Contains(o.Id) && o.TenantId == tenantId && o.DeletedAt == null)
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        row.DeletedAt = now;
                        row.DeletedBy = actorUserId;
                    }

                    // El índice único es parcial (deleted_at IS NULL): la baja debe ir ANTES del alta del reemplazo.
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }

                await AddCoreAsync(add, audit, ct).ConfigureAwait(false);
                return true;
            },
            ct);
}
