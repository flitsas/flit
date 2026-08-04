namespace Flit.Admin.Domain.Auditing;

/// <summary>
/// Proyección de lectura de una fila del rastro unificado de auditoría
/// <c>admin.tenant_config_audit_logs</c> para la consulta global del SuperAdmin
/// (HU #10679). Incluye tanto las columnas de auditoría de configuración (RNF01,
/// ADR-0024) como las de auditoría administrativa/seguridad transversal (HU #10678).
/// </summary>
/// <param name="ChangedByName">Nombre del actor ya resuelto. La UI mostraba el UUID crudo.</param>
/// <param name="ChangedByEmail">Correo del actor, para desambiguar homónimos.</param>
/// <param name="TargetName">Nombre de la entidad afectada (usuario o rol) ya resuelto.</param>
/// <param name="FieldName">Campo modificado en filas de configuración; en operaciones administrativas repite la operación.</param>
/// <param name="OldValue">Valor anterior (jsonb) cuando la operación lo registró.</param>
/// <param name="NewValue">Valor nuevo (jsonb) cuando la operación lo registró.</param>
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
    DateTimeOffset ChangedAt,
    string? ChangedByName = null,
    string? ChangedByEmail = null,
    string? TargetName = null,
    string? FieldName = null,
    string? OldValue = null,
    string? NewValue = null);
