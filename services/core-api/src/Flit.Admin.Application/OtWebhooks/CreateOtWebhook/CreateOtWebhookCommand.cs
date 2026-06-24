using Flit.Admin.Domain.OtWebhooks;

namespace Flit.Admin.Application.OtWebhooks.CreateOtWebhook;

public sealed class CreateOtWebhookCommand
{
    public Guid TenantId { get; init; }

    public Guid? CreatedBy { get; init; }

    public CreateOtWebhookRequest Request { get; init; } = null!;
}

public enum CreateOtWebhookStatus
{
    Created,
    ValidationFailed,
}

public sealed class CreateOtWebhookResult
{
    public CreateOtWebhookStatus Status { get; init; }

    public OtWebhookResponse? Webhook { get; init; }

    public IReadOnlyList<FieldError> Errors { get; init; } = [];

    public static CreateOtWebhookResult Created(OtWebhookResponse webhook) => new()
    {
        Status = CreateOtWebhookStatus.Created,
        Webhook = webhook,
    };

    public static CreateOtWebhookResult ValidationFailed(params FieldError[] errors) => new()
    {
        Status = CreateOtWebhookStatus.ValidationFailed,
        Errors = errors,
    };
}

public sealed record FieldError(string Field, string Message);
