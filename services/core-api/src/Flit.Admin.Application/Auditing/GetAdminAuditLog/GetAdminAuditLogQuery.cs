namespace Flit.Admin.Application.Auditing.GetAdminAuditLog;

/// <summary>
/// Consulta paginada y global (cross-tenant) del rastro unificado de auditoría
/// administrativo/seguridad, exclusiva de SuperAdmin (HU #10679). Refleja los
/// parámetros de query string de <c>GET /api/v1/superadmin/audit</c>. Todos los
/// filtros son opcionales. <c>UserId</c> matchea actor O afectado (R2).
/// </summary>
public sealed record GetAdminAuditLogQuery(
    Guid? UserId,
    Guid? TenantId,
    string? TenantType,
    string? Module,
    string? Operation,
    string? Result,
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    int? Page,
    int? PageSize);
