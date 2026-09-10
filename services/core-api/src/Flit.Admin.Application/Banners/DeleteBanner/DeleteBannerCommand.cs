namespace Flit.Admin.Application.Banners.DeleteBanner;

public sealed class DeleteBannerCommand
{
    public required Guid Id { get; init; }

    public bool Confirm { get; init; }

    public Guid? DeletedBy { get; init; }
}
