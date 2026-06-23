namespace Flit.Admin.Application.OtWebhooks.ListOtApiLogs;

public sealed class ListOtApiLogsQuery
{
    public Guid TenantId { get; init; }

    public string? Direction { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

public sealed class OtApiCallLogResponse
{
    public string Endpoint { get; init; } = string.Empty;

    public string HttpMethod { get; init; } = string.Empty;

    public short? ResponseCode { get; init; }

    public int? DurationMs { get; init; }

    public DateTimeOffset CalledAt { get; init; }

    public Guid? CorrelationId { get; init; }

    public string PayloadHash { get; init; } = string.Empty;
}

public sealed class ListOtApiLogsResult
{
    public IReadOnlyList<OtApiCallLogResponse> Data { get; init; } = [];

    public long TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}
