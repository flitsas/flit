using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.Auditing;

/// <summary>
/// Lectura paginada del rastro unificado de auditoría administrativo/seguridad
/// (HU #10679) sobre <c>admin.tenant_config_audit_logs</c>, con alcance GLOBAL
/// (cross-tenant, exclusivo SuperAdmin). Orden por <c>changed_at</c> descendente.
/// </summary>
public interface IAdminAuditLogRepository
{
    Task<PagedResult<AdminAuditLogEntry>> ListPagedAsync(
        AdminAuditLogFilter filter,
        CancellationToken cancellationToken = default);
}
