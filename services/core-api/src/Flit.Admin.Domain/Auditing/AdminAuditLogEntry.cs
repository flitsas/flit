namespace Flit.Admin.Domain.Auditing;

/// <summary>
/// Proyección de lectura de una fila del rastro unificado de auditoría
/// <c>admin.tenant_config_audit_logs</c> para la consulta global del SuperAdmin
/// (HU #10679). Incluye tanto las columnas de auditoría de configuración (RNF01,
/// ADR-0024) como las de auditoría administrativa/seguridad transversal (HU #10678).
/// </summary>
public sealed record AdminAuditLogEntry(
    Guid Id,
    Guid? TenantId,
    string? TenantType,
    string? Module,
    string EntityName,
    string? Operation,
    string? Result,
    string? ErrorCode,
    Guid? ChangedBy,
    string? TargetEntityType,
    Guid? TargetEntityId,
    string? ClientIp,
    DateTimeOffset ChangedAt);
