namespace Flit.Admin.Application.Companies.TransitOffices.CreateTransitOffice;

/// <summary>
/// Payload del alta de tenant OT (POST /api/v1/admin/transit-office-tenants). Campos
/// en camelCase vía las System.Text.Json Web defaults. Todos nullable para validar
/// explícitamente (un campo ausente devuelve 422, no un 400 de binding).
/// </summary>
/// <param name="TransitOfficeId">Oficina del catálogo <c>catalogs.transit_offices</c> a vincular. Requerido.</param>
/// <param name="LegalName">Razón social (legal_name). Requerido, ≤255.</param>
/// <param name="TaxId">NIT (tax_id). Requerido, ≤20.</param>
/// <param name="Code">Código único del tenant. Requerido, ≤32.</param>
/// <param name="OperationMode">Modo de operación del perfil OT. Opcional (default <c>dashboard</c>).</param>
public sealed record CreateTransitOfficeRequest(
    Guid? TransitOfficeId,
    string? LegalName,
    string? TaxId,
    string? Code,
    string? OperationMode);
