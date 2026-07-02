using Flit.Admin.Domain.Improntas;

namespace Flit.Admin.Application.Improntas.ListImprontas;

/// <summary>
/// Resultado del listado paginado del historial de improntas (HU #10468). Serializado
/// como <c>{ data, totalCount, page, pageSize }</c>, mismo shape que
/// <c>ListTransitOfficeTenantsResult</c>.
/// </summary>
public sealed class ListImprontasResult
{
    public ListImprontasResult(
        IReadOnlyList<ImprontaGenerationListItem> data,
        long totalCount,
        int page,
        int pageSize)
    {
        Data = data;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<ImprontaGenerationListItem> Data { get; }

    public long TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }
}
