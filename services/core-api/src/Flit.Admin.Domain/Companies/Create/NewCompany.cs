namespace Flit.Admin.Domain.Companies.Create;

/// <summary>
/// Datos normalizados para dar de alta una compañía (<c>identity.tenants</c>).
/// Lo construye la capa de aplicación tras validar; el repositorio lo persiste.
/// </summary>
/// <param name="LegalName">Razón social — <c>legal_name</c>.</param>
/// <param name="TaxId">NIT — <c>tax_id</c>.</param>
/// <param name="Code">Código único del tenant — <c>code</c>.</param>
/// <param name="TenantType">Tipo de compañía — <c>tenant_type</c>. Si es de cabeza de grupo
/// (<see cref="HeadTenantTypes"/>), la fila nace con <c>is_group_parent = true</c> (HU #12406).</param>
/// <param name="IsActive">Estado activo inicial — <c>is_active</c>.</param>
/// <param name="CreatedBy">Operador SuperAdmin que crea la compañía (claim sub), si se conoce.</param>
public sealed record NewCompany(
    string LegalName,
    string TaxId,
    string Code,
    string TenantType,
    bool IsActive,
    Guid? CreatedBy)
{
    /// <summary>
    /// HU #12406 — valor que debe llevar <c>identity.tenants.is_group_parent</c>: exactamente «el
    /// tipo es de cabeza» (CHECK <c>ck_tenants_group_parent_by_type</c>). Se deriva aquí, en el
    /// dominio, para que el repositorio no tenga que conocer la regla.
    /// </summary>
    public bool IsGroupParent => HeadTenantTypes.IsHead(TenantType);
}
