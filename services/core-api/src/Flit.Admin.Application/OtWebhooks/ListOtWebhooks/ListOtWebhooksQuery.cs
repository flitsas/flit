namespace Flit.Admin.Application.OtWebhooks.ListOtWebhooks;

public sealed class ListOtWebhooksQuery
{
    public Guid TenantId { get; init; }
}

public sealed class ListOtWebhooksResult
{
    public IReadOnlyList<OtWebhookResponse> Data { get; init; } = [];
}
