namespace Flit.Admin.Domain.Companies.Create;

/// <summary>
/// Datos para dar de alta un cliente hijo bajo una cabeza de grupo (HU #12345 AC1).
/// </summary>
/// <param name="LegalName">Razón social.</param>
/// <param name="TaxId">NIT.</param>
/// <param name="Code">Código único del tenant.</param>
/// <param name="TenantType">Solo <see cref="ChildTenantTypes"/>.</param>
/// <param name="IsActive">Estado activo inicial.</param>
/// <param name="ParentTenantId">Cabeza de grupo que crea al hijo.</param>
/// <param name="CreatedBy">Operador que ejecuta el alta.</param>
public sealed record NewChildCompany(
    string LegalName,
    string TaxId,
    string Code,
    string TenantType,
    bool IsActive,
    Guid ParentTenantId,
    Guid? CreatedBy);
