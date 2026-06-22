namespace Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;

/// <summary>
/// Página del historial de auditoría. Serializado como
/// <c>{ data, totalCount, page, pageSize }</c> (AC4), igual que el listado de compañías.
/// </summary>
public sealed class GetTenantAuditLogResult
{
    public GetTenantAuditLogResult(
        IReadOnlyList<AuditLogEntryResponse> data,
        long totalCount,
        int page,
        int pageSize)
    {
        Data = data;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<AuditLogEntryResponse> Data { get; }

    public long TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }
}
