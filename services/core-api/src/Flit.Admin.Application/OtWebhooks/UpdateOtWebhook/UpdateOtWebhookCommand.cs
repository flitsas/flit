namespace Flit.Admin.Application.OtWebhooks.UpdateOtWebhook;

public sealed class UpdateOtWebhookRequest
{
    public string? TargetUrl { get; init; }

    public bool? IsActive { get; init; }
}

public sealed class UpdateOtWebhookCommand
{
    public Guid TenantId { get; init; }

    public Guid SubscriptionId { get; init; }

    public Guid? ChangedBy { get; init; }

    public UpdateOtWebhookRequest Request { get; init; } = null!;
}

public enum UpdateOtWebhookStatus
{
    Updated,
    NotFound,
    ValidationFailed,
}

public sealed class UpdateOtWebhookResult
{
    public UpdateOtWebhookStatus Status { get; init; }

    public OtWebhookResponse? Webhook { get; init; }

    public IReadOnlyList<CreateOtWebhook.FieldError> Errors { get; init; } = [];

    public static UpdateOtWebhookResult Updated(OtWebhookResponse webhook) => new()
    {
        Status = UpdateOtWebhookStatus.Updated,
        Webhook = webhook,
    };

    public static UpdateOtWebhookResult NotFound() => new()
    {
        Status = UpdateOtWebhookStatus.NotFound,
    };

    public static UpdateOtWebhookResult ValidationFailed(params CreateOtWebhook.FieldError[] errors) => new()
    {
        Status = UpdateOtWebhookStatus.ValidationFailed,
        Errors = errors,
    };
}
