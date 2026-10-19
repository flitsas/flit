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

                return true;
            },
            ct);
}
