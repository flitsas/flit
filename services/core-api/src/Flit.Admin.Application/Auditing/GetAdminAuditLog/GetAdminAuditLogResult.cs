namespace Flit.Admin.Application.Auditing.GetAdminAuditLog;

/// <summary>
/// Página del rastro unificado de auditoría (HU #10679). Serializado como
/// <c>{ data, totalCount, page, pageSize }</c>, igual que el resto de listados
/// paginados del módulo Admin.
/// </summary>
public sealed record GetAdminAuditLogResult(
    IReadOnlyList<AdminAuditLogEntryResponse> Data,
    long TotalCount,
    int Page,
    int PageSize);
