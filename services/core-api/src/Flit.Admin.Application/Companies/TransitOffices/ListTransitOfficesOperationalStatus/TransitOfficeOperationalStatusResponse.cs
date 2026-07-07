namespace Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficesOperationalStatus;

/// <summary>
/// Estado operativo de un organismo de tránsito expuesto por la API (RF01). Una fila
/// por oficina del catálogo; <c>tenantId</c>/<c>estadoActivo</c>/<c>operationMode</c>
/// son nulos cuando la oficina aún no tiene tenant OT (sin alta).
/// </summary>
public sealed record TransitOfficeOperationalStatusResponse(
    Guid Id,
    string Code,
    string Name,
    string DepartmentCode,
    bool HasTenant,
    Guid? TenantId,
    bool? EstadoActivo,
    string? OperationMode);
