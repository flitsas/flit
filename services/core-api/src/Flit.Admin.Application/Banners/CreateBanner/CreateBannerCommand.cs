namespace Flit.Admin.Application.Banners.CreateBanner;

public sealed class CreateBannerCommand
{
    public required string Name { get; init; }

    public string? LinkUrl { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidUntil { get; init; }

    public required string ImageFilename { get; init; }

    public required string ImageContentType { get; init; }

    public required long ImageSizeBytes { get; init; }

    public required Stream ImageContent { get; init; }

    public Guid? CreatedBy { get; init; }
}
