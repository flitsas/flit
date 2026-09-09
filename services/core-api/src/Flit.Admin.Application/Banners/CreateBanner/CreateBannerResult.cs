namespace Flit.Admin.Application.Banners.CreateBanner;

public sealed class CreateBannerResult
{
    private CreateBannerResult(bool isValid, BannerResponse? banner, string? error)
    {
        IsValid = isValid;
        Banner = banner;
        Error = error;
    }

    public bool IsValid { get; }

    public BannerResponse? Banner { get; }

    public string? Error { get; }

    public static CreateBannerResult Success(BannerResponse banner) => new(true, banner, null);

    public static CreateBannerResult Invalid(string error) => new(false, null, error);
}
