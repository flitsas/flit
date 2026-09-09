using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners.ListBanners;

public sealed class ListBannersHandler
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly IBannerRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ListBannersHandler(IBannerRepository repository, TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ListBannersResult> HandleAsync(ListBannersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var filter = new BannerListFilter
        {
            Page = page,
            PageSize = pageSize,
            IncludeDeleted = query.IncludeDeleted ?? false,
        };

        var result = await _repository.ListAsync(filter, cancellationToken).ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();
        var data = result.Items.Select(item => BannerResponse.From(item, now)).ToList();

        return new ListBannersResult(data, result.TotalCount, page, pageSize);
    }

    private static int NormalizePage(int? page) =>
        page is null || page < 1 ? DefaultPage : page.Value;

    private static int NormalizePageSize(int? pageSize)
    {
        if (pageSize is null || pageSize < 1)
        {
            return DefaultPageSize;
        }

        return pageSize.Value > MaxPageSize ? MaxPageSize : pageSize.Value;
    }
}
