namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Proyección de lectura de una fila de <c>admin.tenant_config_audit_logs</c> para la
/// consulta de historial de gobernanza (HU #10192, AC4).
/// </summary>
public sealed record TenantAuditLogEntry(
    string EntityName,
    string FieldName,
    string? OldValue,
    string? NewValue,
    Guid? ChangedBy,
    DateTimeOffset ChangedAt);
