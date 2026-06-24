namespace Flit.Admin.Domain.OtWebhooks;

public interface IOtWebhookSubscriptionRepository
{
    Task<OtWebhookSubscription> CreateAsync(
        Guid tenantId,
        string eventType,
        string targetUrl,
        string secretHash,
        Guid? createdBy,
        CancellationToken cancellationToken = default);

    Task<OtWebhookSubscription?> GetByIdAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<OtWebhookSubscription?> UpdateAsync(
        Guid tenantId,
        Guid subscriptionId,
        string? targetUrl,
        bool? isActive,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OtWebhookSubscription>> ListActiveByEventTypeAsync(
        Guid tenantId,
        string eventType,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OtWebhookSubscription>> ListAllAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Lookup global por ID de suscripción (callback inbound sin JWT).</summary>
    Task<OtWebhookSubscription?> GetBySubscriptionIdAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default);
}
