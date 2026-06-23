using Flit.Admin.Domain.OtWebhooks;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Repositorio de suscripciones webhook OT (HU #10216).</summary>
internal sealed class OtWebhookSubscriptionRepository : IOtWebhookSubscriptionRepository
{
    private readonly FlitDbContext _context;

    public OtWebhookSubscriptionRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<OtWebhookSubscription> CreateAsync(
        Guid tenantId,
        string eventType,
        string targetUrl,
        string secretHash,
        Guid? createdBy,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var entity = new OtWebhookSubscriptionEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EventType = eventType,
                    TargetUrl = targetUrl,
                    SecretHash = secretHash,
                    IsActive = true,
                    CreatedAt = now,
                    CreatedBy = createdBy,
                };

                _context.OtWebhookSubscriptions.Add(entity);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Map(entity);
            },
            cancellationToken);

    public Task<OtWebhookSubscription?> GetByIdAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.OtWebhookSubscriptions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        s => s.TenantId == tenantId && s.Id == subscriptionId,
                        cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : Map(entity);
            },
            cancellationToken);

    public Task<OtWebhookSubscription?> UpdateAsync(
        Guid tenantId,
        Guid subscriptionId,
        string? targetUrl,
        bool? isActive,
        Guid? changedBy,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.OtWebhookSubscriptions
                    .FirstOrDefaultAsync(
                        s => s.TenantId == tenantId && s.Id == subscriptionId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return null;
                }

                if (targetUrl is not null)
                {
                    entity.TargetUrl = targetUrl;
                }

                if (isActive is not null)
                {
                    entity.IsActive = isActive.Value;
                }

                entity.UpdatedAt = DateTimeOffset.UtcNow;
                entity.UpdatedBy = changedBy;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Map(entity);
            },
            cancellationToken);

    public Task<IReadOnlyList<OtWebhookSubscription>> ListActiveByEventTypeAsync(
        Guid tenantId,
        string eventType,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entities = await _context.OtWebhookSubscriptions
                    .AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.IsActive && s.EventType == eventType)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<OtWebhookSubscription>)entities.Select(Map).ToList();
            },
            cancellationToken);

    private static OtWebhookSubscription Map(OtWebhookSubscriptionEntity entity) => new()
    {
        Id = entity.Id,
        TenantId = entity.TenantId,
        EventType = entity.EventType,
        TargetUrl = entity.TargetUrl,
        SecretHash = entity.SecretHash,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
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
