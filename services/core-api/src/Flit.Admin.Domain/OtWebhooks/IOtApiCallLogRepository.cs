using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.OtWebhooks;

public interface IOtApiCallLogRepository
{
    Task<PagedResult<OtApiCallLog>> ListPagedAsync(
        Guid tenantId,
        OtApiCallLogFilter filter,
        CancellationToken cancellationToken = default);

    Task<OtApiCallLog> AppendAsync(
        Guid tenantId,
        string direction,
        string endpoint,
        string httpMethod,
        string payloadHash,
        short? responseCode,
        int? durationMs,
        Guid? correlationId,
        CancellationToken cancellationToken = default);
}
