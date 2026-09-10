namespace Flit.Admin.Application.Banners.SetBannerActive;

public enum SetBannerActiveOutcome
{
    Updated,
    NotFound,
}

public sealed class SetBannerActiveResult
{
    private SetBannerActiveResult(SetBannerActiveOutcome outcome)
    {
        Outcome = outcome;
    }

    public SetBannerActiveOutcome Outcome { get; }

    public static SetBannerActiveResult Updated() => new(SetBannerActiveOutcome.Updated);

    public static SetBannerActiveResult NotFound() => new(SetBannerActiveOutcome.NotFound);
}
