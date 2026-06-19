using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.ListCompanies;

/// <summary>
/// Resultado del listado paginado. Serializado como
/// <c>{ data, totalCount, page, pageSize }</c> (HU #10189, AC1).
/// </summary>
public sealed class ListCompaniesResult
{
    public ListCompaniesResult(
        IReadOnlyList<CompanyListItem> data,
        long totalCount,
        int page,
        int pageSize)
    {
        Data = data;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<CompanyListItem> Data { get; }

    public long TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }
}
