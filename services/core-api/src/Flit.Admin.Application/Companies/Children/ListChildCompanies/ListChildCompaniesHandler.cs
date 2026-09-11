using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.Children.ListChildCompanies;

/// <summary>Listado de clientes hijos de una cabeza de grupo (panel de red).</summary>
public sealed class ListChildCompaniesHandler
{
    private readonly ICompanyHierarchyRepository _hierarchy;

    public ListChildCompaniesHandler(ICompanyHierarchyRepository hierarchy)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
    }

    public Task<IReadOnlyList<CompanyChildListItem>> HandleAsync(
        Guid headTenantId,
        CancellationToken cancellationToken = default) =>
        _hierarchy.ListChildrenAsync(headTenantId, cancellationToken);

    public Task<CompanyChildListItem?> GetAsync(
        Guid headTenantId,
        Guid childTenantId,
        CancellationToken cancellationToken = default) =>
        _hierarchy.GetChildAsync(headTenantId, childTenantId, cancellationToken);
}
