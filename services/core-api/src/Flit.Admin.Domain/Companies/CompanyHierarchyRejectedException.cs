namespace Flit.Admin.Domain.Companies;

/// <summary>
/// HU #12406 — la base de datos rechazó el cambio de tipo de una compañía porque la jerarquía de
/// clientes lo impide (<c>tr_tenants_hierarchy</c>: la clase de una cabeza con hijos vigentes es
/// inmutable y una cabeza con hijos no puede dejar de serlo). La capa de aplicación lo traduce a
/// un 422 con mensaje explícito sobre <c>tenantType</c>; nunca a un 500.
/// </summary>
public sealed class CompanyHierarchyRejectedException : Exception
{
    public CompanyHierarchyRejectedException(Guid tenantId, string detail)
        : base($"La compañía {tenantId} no puede cambiar de tipo: {detail}")
    {
        TenantId = tenantId;
        Detail = detail;
    }

    public Guid TenantId { get; }

    /// <summary>Texto del rechazo del motor (sin datos sensibles: ids y tipos).</summary>
    public string Detail { get; }
}
