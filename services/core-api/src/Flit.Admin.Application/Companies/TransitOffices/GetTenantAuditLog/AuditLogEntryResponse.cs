namespace Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;

/// <summary>
/// Entrada del historial de auditoría expuesta por la API (AC4): campo modificado,
/// valores anterior/nuevo, operador y timestamp.
/// </summary>
public sealed record AuditLogEntryResponse(
    string EntityName,
    string FieldName,
    string? OldValue,
    string? NewValue,
    Guid? ChangedBy,
    DateTimeOffset ChangedAt);
