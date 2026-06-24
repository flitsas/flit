using Flit.Admin.Domain.OtWebhooks;

namespace Flit.Admin.Application.OtWebhooks;

public sealed class CreateOtWebhookRequest
{
    public string EventType { get; init; } = string.Empty;

    public string TargetUrl { get; init; } = string.Empty;

    public string Secret { get; init; } = string.Empty;
}

public sealed class OtWebhookResponse
{
    public Guid Id { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string TargetUrl { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

public static class OtWebhookMapper
{
    public static OtWebhookResponse ToResponse(OtWebhookSubscription subscription) => new()
    {
        Id = subscription.Id,
        EventType = subscription.EventType,
        TargetUrl = subscription.TargetUrl,
        IsActive = subscription.IsActive,
        CreatedAt = subscription.CreatedAt,
        UpdatedAt = subscription.UpdatedAt,
    };
}
