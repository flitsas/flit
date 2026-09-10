namespace Flit.Admin.Application.Banners.SetBannerActive;

public sealed class SetBannerActiveCommand
{
    public required Guid Id { get; init; }

    public required bool IsActive { get; init; }

    public Guid? UpdatedBy { get; init; }
}
