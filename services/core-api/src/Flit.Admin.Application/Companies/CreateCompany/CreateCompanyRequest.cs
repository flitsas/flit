namespace Flit.Admin.Application.Companies.CreateCompany;

/// <summary>
/// Payload del alta de compañía (POST /api/v1/admin/companies). Campos en camelCase
/// vía las System.Text.Json Web defaults. Todos nullable para validar explícitamente
/// (un campo ausente devuelve 422, no un 400 de binding).
/// </summary>
/// <param name="RazonSocial">Razón social (legal_name). Requerido, ≤255.</param>
/// <param name="Nit">NIT (tax_id). Requerido, ≤20.</param>
/// <param name="TenantType">Tipo de compañía: RENTING | CONCESIONARIO | FLIT | CONCESION | MARCA_BLANCA
/// (los dos últimos son cabezas de grupo, HU #12406).</param>
/// <param name="EstadoActivo">Estado activo inicial. Opcional (default true).</param>
/// <remarks>
/// HU #13605: ya no lleva <c>code</c>. Lo asigna Postgres (consecutivo COMP-XXXX); si un cliente viejo lo envía,
/// el binder lo descarta como cualquier propiedad desconocida.
/// </remarks>
public sealed record CreateCompanyRequest(
    string? RazonSocial,
    string? Nit,
    string? TenantType,
    bool? EstadoActivo);
