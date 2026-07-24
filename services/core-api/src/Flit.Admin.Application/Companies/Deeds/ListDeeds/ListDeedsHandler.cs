using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.Deeds.ListDeeds;

/// <summary>
/// Listado paginado de escrituras de un tenant (HU #10902). Devuelve el envelope
/// <c>{ data, totalCount, page, pageSize }</c>. Sanea la paginación (page ≥ 1; pageSize en [1, 100],
/// default 20) para acotar la carga server-side.
/// </summary>
public sealed class ListDeedsHandler
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IDeedReader _reader;

    public ListDeedsHandler(IDeedReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<PagedDeedsResponse> HandleAsync(
        ListDeedsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1
            ? DefaultPageSize
            : Math.Min(query.PageSize, MaxPageSize);

        var result = await _reader
            .ListPagedAsync(query.TenantId, page, pageSize, cancellationToken).ConfigureAwait(false);

        var data = result.Items.Select(DeedResponse.From).ToList();
        return new PagedDeedsResponse(data, result.TotalCount, page, pageSize);
    }
}
