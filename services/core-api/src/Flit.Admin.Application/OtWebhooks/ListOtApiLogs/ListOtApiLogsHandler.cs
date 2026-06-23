using Flit.Admin.Domain.OtWebhooks;

namespace Flit.Admin.Application.OtWebhooks.ListOtApiLogs;

/// <summary>Consulta paginada de bitácora API OT (HU #10216 AC4/AC5).</summary>
public sealed class ListOtApiLogsHandler
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly IOtApiCallLogRepository _repository;

    public ListOtApiLogsHandler(IOtApiCallLogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListOtApiLogsResult> HandleAsync(
        ListOtApiLogsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var filter = new OtApiCallLogFilter
        {
            Direction = query.Direction,
            From = query.From,
            To = query.To,
            Page = page,
            PageSize = pageSize,
        };

        var result = await _repository
            .ListPagedAsync(query.TenantId, filter, cancellationToken)
            .ConfigureAwait(false);

        var data = result.Items.Select(l => new OtApiCallLogResponse
        {
            Endpoint = l.Endpoint,
            HttpMethod = l.HttpMethod,
            ResponseCode = l.ResponseCode,
            DurationMs = l.DurationMs,
            CalledAt = l.CalledAt,
            CorrelationId = l.CorrelationId,
            PayloadHash = l.PayloadHash,
        }).ToList();

        return new ListOtApiLogsResult
        {
            Data = data,
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static int NormalizePage(int? page) =>
        page is null or < 1 ? DefaultPage : page.Value;

    private static int NormalizePageSize(int? pageSize)
    {
        if (pageSize is null or < 1)
        {
            return DefaultPageSize;
        }

        return pageSize.Value > MaxPageSize ? MaxPageSize : pageSize.Value;
    }
}
