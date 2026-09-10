namespace Flit.Admin.Application.Banners.DeleteBanner;

public enum DeleteBannerOutcome
{
    Deleted,
    ConfirmationRequired,
    NotFound,
}

public sealed class DeleteBannerResult
{
    private DeleteBannerResult(DeleteBannerOutcome outcome)
    {
        Outcome = outcome;
    }

    public DeleteBannerOutcome Outcome { get; }

    public static DeleteBannerResult Deleted() => new(DeleteBannerOutcome.Deleted);

    public static DeleteBannerResult ConfirmationRequired() => new(DeleteBannerOutcome.ConfirmationRequired);

    public static DeleteBannerResult NotFound() => new(DeleteBannerOutcome.NotFound);
}
