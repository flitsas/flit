namespace Flit.Admin.Application.Banners.ListActiveBanners;

/// <summary>Resultado del listado publico de banners activos (HU #12240, AC1/AC3).</summary>
public sealed class ListActiveBannersResult
{
    public ListActiveBannersResult(IReadOnlyList<ActiveBannerResponse> data)
    {
        Data = data;
    }

    /// <summary>Lista vacia (nunca error) cuando no hay banners activos vigentes (AC3).</summary>
    public IReadOnlyList<ActiveBannerResponse> Data { get; }
}
