namespace Flit.Admin.Application.Banners.ListBanners;

public sealed class ListBannersResult
{
    public ListBannersResult(IReadOnlyList<BannerResponse> data, long totalCount, int page, int pageSize)
    {
        Data = data;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<BannerResponse> Data { get; }

    public long TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }
}
