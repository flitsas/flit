namespace Flit.Admin.Domain.Companies.Create;

/// <summary>
/// Datos normalizados para dar de alta una compañía (<c>identity.tenants</c>).
/// Lo construye la capa de aplicación tras validar; el repositorio lo persiste.
/// </summary>
/// <param name="LegalName">Razón social — <c>legal_name</c>.</param>
/// <param name="TaxId">NIT — <c>tax_id</c>.</param>
/// <param name="Code">Código único del tenant — <c>code</c>.</param>
/// <param name="TenantType">Tipo de compañía — <c>tenant_type</c>.</param>
/// <param name="IsActive">Estado activo inicial — <c>is_active</c>.</param>
/// <param name="CreatedBy">Operador SuperAdmin que crea la compañía (claim sub), si se conoce.</param>
public sealed record NewCompany(
    string LegalName,
    string TaxId,
    string Code,
    string TenantType,
    bool IsActive,
    Guid? CreatedBy);
