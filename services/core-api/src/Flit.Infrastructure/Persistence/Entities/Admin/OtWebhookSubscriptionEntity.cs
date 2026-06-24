namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>Suscripción webhook OT — <c>admin.ot_webhook_subscriptions</c> (HU #10152 / #10216).</summary>
public sealed class OtWebhookSubscriptionEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string TargetUrl { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
