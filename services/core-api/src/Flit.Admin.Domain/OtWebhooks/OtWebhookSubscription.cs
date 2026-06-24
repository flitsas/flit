namespace Flit.Admin.Domain.OtWebhooks;

/// <summary>Suscripción webhook OT por tenant (HU #10216).</summary>
public sealed class OtWebhookSubscription
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string TargetUrl { get; init; } = string.Empty;

    /// <summary>SHA-256 hex del secreto compartido (clave HMAC en dispatch).</summary>
    public string SecretHash { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
