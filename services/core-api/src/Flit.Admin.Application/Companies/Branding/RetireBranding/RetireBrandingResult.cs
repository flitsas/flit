using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.RetireBranding;

public enum RetireBrandingOutcome
{
    Retired,
    NotFound,
}

public sealed class RetireBrandingResult
{
    private RetireBrandingResult(RetireBrandingOutcome outcome, TenantBranding? branding)
    {
        Outcome = outcome;
        Branding = branding;
    }

    public RetireBrandingOutcome Outcome { get; }

    public TenantBranding? Branding { get; }

    public static RetireBrandingResult Success(TenantBranding branding) => new(RetireBrandingOutcome.Retired, branding);

    public static RetireBrandingResult NotFound() => new(RetireBrandingOutcome.NotFound, null);
}
