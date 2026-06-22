using Flit.Admin.Domain.OtProfile;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de feature flags OT (HU #10215). Cambios auditados vía trigger
/// <c>tr_ot_feature_flags_audit</c> en PostgreSQL.
/// </summary>
internal sealed class OtFeatureFlagRepository : IOtFeatureFlagRepository
{
    private readonly FlitDbContext _context;

    public OtFeatureFlagRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<OtFeatureFlag>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entities = await _context.OtFeatureFlags
                    .AsNoTracking()
                    .Where(f => f.TenantId == tenantId)
                    .OrderBy(f => f.FlagKey)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return entities.Select(Map).ToList();
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<OtFeatureFlag?> GetByIdAsync(
        Guid tenantId,
        Guid flagId,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.OtFeatureFlags
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.Id == flagId, cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : Map(entity);
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<OtFeatureFlag?> UpdateEnabledAsync(
        Guid tenantId,
        Guid flagId,
        bool isEnabled,
        Guid? changedBy,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.OtFeatureFlags
                    .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.Id == flagId, cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return null;
                }

                var now = DateTimeOffset.UtcNow;
                entity.IsEnabled = isEnabled;
                entity.UpdatedAt = now;
                entity.UpdatedBy = changedBy;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Map(entity);
            },
            cancellationToken).ConfigureAwait(false);

    private static OtFeatureFlag Map(OtFeatureFlagEntity entity) => new()
    {
        Id = entity.Id,
        TenantId = entity.TenantId,
        FlagKey = entity.FlagKey,
        IsEnabled = entity.IsEnabled,
        ConfigJson = entity.Config,
    };

    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid tenantId,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }
}
