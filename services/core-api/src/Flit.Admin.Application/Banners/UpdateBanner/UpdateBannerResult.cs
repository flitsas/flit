namespace Flit.Admin.Application.Banners.UpdateBanner;

public enum UpdateBannerOutcome
{
    Updated,
    ValidationFailed,
    NotFound,
}

public sealed class UpdateBannerResult
{
    private UpdateBannerResult(UpdateBannerOutcome outcome, BannerResponse? banner, string? error)
    {
        Outcome = outcome;
        Banner = banner;
        Error = error;
    }

    public UpdateBannerOutcome Outcome { get; }

    public BannerResponse? Banner { get; }

    public string? Error { get; }

    public static UpdateBannerResult Updated(BannerResponse banner) =>
        new(UpdateBannerOutcome.Updated, banner, null);

    public static UpdateBannerResult ValidationFailed(string error) =>
        new(UpdateBannerOutcome.ValidationFailed, null, error);

    public static UpdateBannerResult NotFound() =>
        new(UpdateBannerOutcome.NotFound, null, null);
}
