namespace Flit.Admin.Application.Companies.Branding.UploadBrandLogo;

public sealed class UploadBrandLogoCommand
{
    public required Guid TenantId { get; init; }

    public required string Filename { get; init; }

    public required Stream Content { get; init; }

    public Guid? ChangedBy { get; init; }
}
