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
    DateTimeOffset ChangedAt);
