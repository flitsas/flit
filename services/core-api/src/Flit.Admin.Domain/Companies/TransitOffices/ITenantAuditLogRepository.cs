using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Lectura paginada del historial de auditoría de gobernanza de un tenant
/// (HU #10192, AC4). Orden por <c>changed_at</c> descendente.
/// </summary>
public interface ITenantAuditLogRepository
{
    Task<PagedResult<TenantAuditLogEntry>> ListPagedAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
