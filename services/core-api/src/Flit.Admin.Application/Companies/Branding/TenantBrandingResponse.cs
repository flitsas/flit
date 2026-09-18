using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding;

public sealed record BrandColorsResponse(string Primary, string Secondary, string OnPrimary);

public sealed record BrandingSnapshotResponse(string? PlatformName, BrandColorsResponse? Colors, Guid? LogoId);

public sealed record BrandingPublishedByResponse(Guid UserId);

public sealed record BrandingCompletenessResponse(bool IsComplete, IReadOnlyList<string> Missing);

/// <summary>Forma de <c>GET/PUT .../branding</c> (SuperAdmin y autogestión, HU #12412 AC1/AC3/AC4).</summary>
public sealed record TenantBrandingResponse(
    Guid TenantId,
    BrandingSnapshotResponse Draft,
    BrandingSnapshotResponse? Published,
    int PublishedVersion,
    DateTimeOffset? PublishedAt,
    BrandingPublishedByResponse? PublishedBy,
    bool HasUnpublishedChanges,
    string? LogoUrl,
    BrandingCompletenessResponse Completeness,
    long RowVersion)
{
    public static TenantBrandingResponse From(TenantBranding branding)
    {
        ArgumentNullException.ThrowIfNull(branding);

        return new TenantBrandingResponse(
            branding.TenantId,
            ToSnapshot(branding.Draft)!,
            ToSnapshot(branding.Published),
            branding.PublishedVersion,
            branding.PublishedAt,
            branding.PublishedBy is { } userId ? new BrandingPublishedByResponse(userId) : null,
            branding.HasUnpublishedChanges,
            branding.Draft.LogoId is { } logoId ? $"/api/v1/public/branding/logos/{logoId}" : null,
            new BrandingCompletenessResponse(branding.Draft.MissingFields().Count == 0, branding.Draft.MissingFields()),
            branding.RowVersion);
    }

    private static BrandingSnapshotResponse? ToSnapshot(BrandingDraft? draft) =>
        draft is null
            ? null
            : new BrandingSnapshotResponse(
                draft.PlatformName,
                draft.Colors is { } colors ? new BrandColorsResponse(colors.Primary, colors.Secondary, colors.OnPrimary) : null,
                draft.LogoId);
}

public sealed record BrandLogoResponse(
    Guid LogoId,
    int Version,
    string ContentType,
    int Width,
    int Height,
    long SizeBytes,
    string Sha256,
    string LogoUrl)
{
    public static BrandLogoResponse From(TenantBrandLogoVersion logo)
    {
        ArgumentNullException.ThrowIfNull(logo);
        return new BrandLogoResponse(
            logo.Id,
            logo.Version,
            logo.ContentType,
            logo.WidthPx,
            logo.HeightPx,
            logo.SizeBytes,
            logo.StorageSha256,
            $"/api/v1/public/branding/logos/{logo.Id}");
    }
}
