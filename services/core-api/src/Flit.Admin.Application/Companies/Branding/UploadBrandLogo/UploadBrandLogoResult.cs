using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.UploadBrandLogo;

public enum UploadBrandLogoOutcome
{
    Uploaded,
    Invalid,
    TenantNotMarcaBlanca,
}

public sealed class UploadBrandLogoResult
{
    private UploadBrandLogoResult(
        UploadBrandLogoOutcome outcome,
        TenantBrandLogoVersion? logo,
        IReadOnlyList<BrandAssetValidationError> errors)
    {
        Outcome = outcome;
        Logo = logo;
        Errors = errors;
    }

    public UploadBrandLogoOutcome Outcome { get; }

    public TenantBrandLogoVersion? Logo { get; }

    public IReadOnlyList<BrandAssetValidationError> Errors { get; }

    public static UploadBrandLogoResult Success(TenantBrandLogoVersion logo) =>
        new(UploadBrandLogoOutcome.Uploaded, logo, []);

    public static UploadBrandLogoResult Invalid(IReadOnlyList<BrandAssetValidationError> errors) =>
        new(UploadBrandLogoOutcome.Invalid, null, errors);

    public static UploadBrandLogoResult TenantNotMarcaBlanca() =>
        new(UploadBrandLogoOutcome.TenantNotMarcaBlanca, null, []);
}
