using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13646 (ADR-0073, FB-15): purga de permisos individuales al cambiar de rol. Una fila de bitácora
/// <c>ROLE_CHANGE_PURGE</c> por permiso (porque la bitácora guarda slug y <c>value_before</c> por fila), todas con el
/// mismo <c>batch_id</c> para reconstruir el lote. Se une a la transacción del cambio de rol: si este falla, no se purga.
/// </summary>
internal sealed class RoleChangeOverridesPurger(FlitDbContext db) : IRoleChangeOverridesPurger
{
    public const string PurgeAction = "ROLE_CHANGE_PURGE";

    public async Task RunAtomicallyAsync(Func<Task> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
        {
            await work().ConfigureAwait(false);
            return;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await work().ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    public Task<int> PurgeAsync(Guid tenantId, Guid userId, string productCode, Guid actorUserId, CancellationToken ct) =>
        TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () =>
            {
                var rows = await db.UserPermissionOverrides
                    .Where(o => o.TenantId == tenantId && o.UserId == userId && o.ProductCode == productCode && o.DeletedAt == null)
                    .ToListAsync(ct).ConfigureAwait(false);
                if (rows.Count == 0)
                {
                    return 0;
                }

                var permissionIds = rows.Select(r => r.PermissionId).Distinct().ToList();
                var catalog = await (
                    from p in db.RbacActions.AsNoTracking()
                    join m in db.SecurityModules.AsNoTracking() on p.ModuleId equals m.Id
                    where permissionIds.Contains(p.Id)
                    select new { p.Id, p.Slug, ModuleCode = m.Code })
                    .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);

                var now = DateTimeOffset.UtcNow;
                var batchId = Guid.NewGuid();
                foreach (var row in rows)
                {
                    row.DeletedAt = now;
                    row.DeletedBy = actorUserId;
                    catalog.TryGetValue(row.PermissionId, out var perm);
                    db.UserPermissionAudits.Add(new UserPermissionAuditEntity
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        ActorUserId = actorUserId,
                        TargetUserId = userId,
                        ProductCode = productCode,
                        ModuleCode = perm?.ModuleCode,
                        PermissionSlug = perm?.Slug ?? row.PermissionId.ToString(),
                        Action = PurgeAction,
                        Outcome = "OK",
                        ValueBefore = row.Effect,
                        ValueAfter = null,
                        BatchId = batchId,
                        CreatedAt = now,
                    });
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return rows.Count;
            },
            ct);
}
