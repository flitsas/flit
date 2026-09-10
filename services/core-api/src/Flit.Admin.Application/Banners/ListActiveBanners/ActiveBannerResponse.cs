using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners.ListActiveBanners;

/// <summary>Item del listado publico (HU #12240, AC1). Serializado como <c>{ id, name, linkUrl }</c>.</summary>
public sealed class ActiveBannerResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? LinkUrl { get; init; }

    public static ActiveBannerResponse From(ActiveBannerItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        LinkUrl = item.LinkUrl,
    };
}
