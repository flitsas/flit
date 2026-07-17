namespace Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficesOperationalStatus;

/// <summary>
/// Estado operativo de un organismo de tránsito expuesto por la API (RF01). Una fila
/// por oficina del catálogo; <c>tenantId</c>/<c>estadoActivo</c>/<c>operationMode</c>
/// son nulos cuando la oficina aún no tiene tenant OT (sin alta).
/// </summary>
/// <remarks>
/// Los campos <c>divipoCode</c> y <c>quipux*</c> (HU #10710) son del CATÁLOGO y describen a la
/// secretaría DESTINO a la que FLIT radica por Quipux; existen para las 317 oficinas, tengan o
/// no tenant OT. <c>operationMode</c>, en cambio, solo existe para las 12 que son clientes de
/// FLIT y describe si su consola queda en solo lectura. Son conceptos distintos.
/// </remarks>
public sealed record TransitOfficeOperationalStatusResponse(
    Guid Id,
    string Code,
    string Name,
    string DepartmentCode,
    bool HasTenant,
    Guid? TenantId,
    bool? EstadoActivo,
    string? OperationMode,
    string? DivipoCode,
    bool QuipuxRegistration,
    bool QuipuxTransfer,
    bool QuipuxOther);
