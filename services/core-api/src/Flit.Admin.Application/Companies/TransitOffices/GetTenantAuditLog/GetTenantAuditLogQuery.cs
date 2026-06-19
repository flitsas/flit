namespace Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;

/// <summary>Consulta paginada del historial de auditoría de gobernanza (AC4).</summary>
public sealed class GetTenantAuditLogQuery
{
    public required Guid TenantId { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}
