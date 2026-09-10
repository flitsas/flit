namespace Flit.Admin.Application.Banners.ListBanners;

public sealed class ListBannersQuery
{
    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public bool? IncludeDeleted { get; init; }
}
