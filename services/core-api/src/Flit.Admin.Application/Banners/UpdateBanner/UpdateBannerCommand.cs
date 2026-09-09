namespace Flit.Admin.Application.Banners.UpdateBanner;

public sealed class UpdateBannerCommand
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? LinkUrl { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidUntil { get; init; }

    public string? ImageFilename { get; init; }

    public string? ImageContentType { get; init; }

    public long? ImageSizeBytes { get; init; }

    public Stream? ImageContent { get; init; }

    public Guid? UpdatedBy { get; init; }
}
