using Flit.Admin.Domain.Companies.Create;

namespace Flit.Admin.Domain.Companies;

/// <summary>
/// Repositorio de jerarquía padre-hija sobre <c>identity.tenants</c> (HU #12345, #12355).
/// </summary>
public interface ICompanyHierarchyRepository
{
    /// <summary>Información de jerarquía del tenant, o <c>null</c> si no existe.</summary>
    Task<CompanyHierarchyInfo?> GetHierarchyInfoAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Clientes cuyo <c>parent_tenant_id</c> es <paramref name="headTenantId"/>.</summary>
    Task<IReadOnlyList<CompanyChildListItem>> ListChildrenAsync(
        Guid headTenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Identidad de un hijo directo. <c>null</c> si no existe o no cuelga de la cabeza.
    /// </summary>
    Task<CompanyChildListItem?> GetChildAsync(
        Guid headTenantId,
        Guid childTenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Alta de un cliente hijo vinculado a <paramref name="company"/>.ParentTenantId.
    /// </summary>
    Task<CompanyListItem> CreateChildAsync(
        NewChildCompany company,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vincula o desvincula un tenant existente (SuperAdmin, HU #12355). Devuelve <c>null</c> si el
    /// hijo no existe. Lanza <see cref="CompanyLinkRejectedException"/> si la base rechaza el vínculo.
    /// </summary>
    Task<CompanyListItem?> SetParentAsync(
        Guid childTenantId,
        Guid? parentTenantId,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}
