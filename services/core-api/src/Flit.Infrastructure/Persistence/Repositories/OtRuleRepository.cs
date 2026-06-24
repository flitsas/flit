using Flit.Admin.Domain.OtRules;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reglas OT en <c>admin.ot_feature_flags</c> con <c>flag_key = rule:{'{uuid}'}</c> (HU #10221).
/// </summary>
internal sealed class OtRuleRepository : IOtRuleRepository
{
    private readonly FlitDbContext _context;

    public OtRuleRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<OtRule> CreateAsync(
        Guid tenantId,
        string name,
        IReadOnlyList<OtRuleCondition> conditions,
        string logic,
        OtRuleAction action,
        Guid? createdBy,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var id = Guid.NewGuid();
                var now = DateTimeOffset.UtcNow;
                var entity = new OtFeatureFlagEntity
                {
                    Id = id,
                    TenantId = tenantId,
                    FlagKey = $"{OtRuleConstants.FlagKeyPrefix}{id}",
                    IsEnabled = true,
                    Config = OtRuleConfigSerializer.Serialize(name, conditions, logic, action),
                    CreatedAt = now,
                    CreatedBy = createdBy,
                };

                _context.OtFeatureFlags.Add(entity);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return OtRuleConfigSerializer.Parse(entity.Id, entity.TenantId, entity.IsEnabled, entity.Config);
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<OtRule?> GetByIdAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.OtFeatureFlags
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        f => f.TenantId == tenantId && f.Id == ruleId && f.FlagKey.StartsWith(OtRuleConstants.FlagKeyPrefix),
                        cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : OtRuleConfigSerializer.Parse(entity.Id, entity.TenantId, entity.IsEnabled, entity.Config);
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<OtRule>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entities = await _context.OtFeatureFlags
                    .AsNoTracking()
                    .Where(f => f.TenantId == tenantId && f.FlagKey.StartsWith(OtRuleConstants.FlagKeyPrefix))
                    .OrderBy(f => f.CreatedAt)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return entities.Select(e => OtRuleConfigSerializer.Parse(e.Id, e.TenantId, e.IsEnabled, e.Config)).ToList();
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<OtRule>> ListEnabledByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entities = await _context.OtFeatureFlags
                    .AsNoTracking()
                    .Where(f => f.TenantId == tenantId
                        && f.FlagKey.StartsWith(OtRuleConstants.FlagKeyPrefix)
                        && f.IsEnabled)
                    .OrderBy(f => f.CreatedAt)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return entities.Select(e => OtRuleConfigSerializer.Parse(e.Id, e.TenantId, e.IsEnabled, e.Config)).ToList();
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<OtRule?> UpdateEnabledAsync(
        Guid tenantId,
        Guid ruleId,
        bool isEnabled,
        Guid? changedBy,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.OtFeatureFlags
                    .FirstOrDefaultAsync(
                        f => f.TenantId == tenantId && f.Id == ruleId && f.FlagKey.StartsWith(OtRuleConstants.FlagKeyPrefix),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return null;
                }

                entity.IsEnabled = isEnabled;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                entity.UpdatedBy = changedBy;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return OtRuleConfigSerializer.Parse(entity.Id, entity.TenantId, entity.IsEnabled, entity.Config);
            },
            cancellationToken).ConfigureAwait(false);

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
