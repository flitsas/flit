namespace Flit.Admin.Application.Companies.CreateCompany;

/// <summary>
/// Payload del alta de compañía (POST /api/v1/admin/companies). Campos en camelCase
/// vía las System.Text.Json Web defaults. Todos nullable para validar explícitamente
/// (un campo ausente devuelve 422, no un 400 de binding).
/// </summary>
/// <param name="RazonSocial">Razón social (legal_name). Requerido, ≤255.</param>
/// <param name="Nit">NIT (tax_id). Requerido, ≤20.</param>
/// <param name="Code">Código único del tenant. Requerido, ≤32.</param>
/// <param name="TenantType">Tipo de compañía: RENTING | CONCESIONARIO | FLIT.</param>
/// <param name="EstadoActivo">Estado activo inicial. Opcional (default true).</param>
public sealed record CreateCompanyRequest(
    string? RazonSocial,
    string? Nit,
    string? Code,
    string? TenantType,
    bool? EstadoActivo);
