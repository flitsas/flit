using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.UpsertBrandingDraft;

public enum UpsertBrandingDraftOutcome
{
    Saved,
    Invalid,
    Conflict,
    TenantNotMarcaBlanca,
}

public sealed class UpsertBrandingDraftResult
{
    private UpsertBrandingDraftResult(
        UpsertBrandingDraftOutcome outcome,
        TenantBranding? branding,
        IReadOnlyList<BrandAssetValidationError> errors)
    {
        Outcome = outcome;
        Branding = branding;
        Errors = errors;
    }

    public UpsertBrandingDraftOutcome Outcome { get; }

    public TenantBranding? Branding { get; }

    public IReadOnlyList<BrandAssetValidationError> Errors { get; }

    public static UpsertBrandingDraftResult Success(TenantBranding branding) =>
        new(UpsertBrandingDraftOutcome.Saved, branding, []);

    public static UpsertBrandingDraftResult Invalid(IReadOnlyList<BrandAssetValidationError> errors) =>
        new(UpsertBrandingDraftOutcome.Invalid, null, errors);

    public static UpsertBrandingDraftResult Conflict() =>
        new(UpsertBrandingDraftOutcome.Conflict, null, []);

    public static UpsertBrandingDraftResult TenantNotMarcaBlanca() =>
        new(UpsertBrandingDraftOutcome.TenantNotMarcaBlanca, null, []);
}
