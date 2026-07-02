using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficeTenants;

/// <summary>
/// Resultado del listado paginado de tenants OT. Serializado como
/// <c>{ data, totalCount, page, pageSize }</c>, mismo shape que
/// <c>ListCompaniesResult</c>.
/// </summary>
public sealed class ListTransitOfficeTenantsResult
{
    public ListTransitOfficeTenantsResult(
        IReadOnlyList<TransitOfficeTenantItem> data,
        long totalCount,
        int page,
        int pageSize)
    {
        Data = data;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<TransitOfficeTenantItem> Data { get; }

    public long TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }
}
