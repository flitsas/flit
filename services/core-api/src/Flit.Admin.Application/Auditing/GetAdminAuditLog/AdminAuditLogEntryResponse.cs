namespace Flit.Admin.Application.Auditing.GetAdminAuditLog;

/// <summary>
/// Entrada del rastro unificado de auditoría expuesta por la API (HU #10679): mínimo
/// exigido (usuario/actor, fecha/hora, IP, operación, resultado) más las columnas
/// transversales (tenant, tenant_type, módulo, entidad afectada) del rastro unificado.
/// </summary>
public sealed record AdminAuditLogEntryResponse(
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
    // Resueltos por el repositorio para que el consumidor no tenga que mostrar UUIDs.
    string? ChangedByName = null,
    string? ChangedByEmail = null,
    string? TargetName = null,
    string? FieldName = null,
    string? OldValue = null,
    string? NewValue = null);
