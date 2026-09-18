using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.PublishBranding;

public enum PublishBrandingOutcome
{
    Published,
    NotFound,
    Conflict,
    Incomplete,
}

public sealed class PublishBrandingResult
{
    private PublishBrandingResult(PublishBrandingOutcome outcome, TenantBranding? branding, IReadOnlyList<string> missing)
    {
        Outcome = outcome;
        Branding = branding;
        Missing = missing;
    }

    public PublishBrandingOutcome Outcome { get; }

    public TenantBranding? Branding { get; }

    public IReadOnlyList<string> Missing { get; }

    public static PublishBrandingResult Success(TenantBranding branding) =>
        new(PublishBrandingOutcome.Published, branding, []);

    public static PublishBrandingResult NotFound() => new(PublishBrandingOutcome.NotFound, null, []);

    public static PublishBrandingResult Conflict() => new(PublishBrandingOutcome.Conflict, null, []);

    public static PublishBrandingResult Incomplete(IReadOnlyList<string> missing) =>
        new(PublishBrandingOutcome.Incomplete, null, missing);
}
