namespace Flit.Admin.Application.Companies.LegalRepresentatives.ListLegalRepresentatives;

/// <summary>
/// Página de representantes legales. Serializado como <c>{ data, totalCount, page, pageSize }</c>
/// (HU #10901, AC1), igual que el listado de compañías y el audit log.
/// </summary>
public sealed class ListLegalRepresentativesResult
{
    public ListLegalRepresentativesResult(
        IReadOnlyList<LegalRepresentativeResponse> data,
        long totalCount,
        int page,
        int pageSize)
    {
        Data = data;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<LegalRepresentativeResponse> Data { get; }

    public long TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }
}
