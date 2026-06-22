namespace Flit.Admin.Application.DocumentTypes.ListDocumentTypes;

/// <summary>
/// Resultado del listado paginado. Serializado como
/// <c>{ data, totalCount, page, pageSize }</c> (HU #10193, AC2).
/// </summary>
public sealed class ListDocumentTypesResult
{
    public ListDocumentTypesResult(
        IReadOnlyList<DocumentTypeResponse> data,
        long totalCount,
        int page,
        int pageSize)
    {
        Data = data;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<DocumentTypeResponse> Data { get; }

    public long TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }
}
