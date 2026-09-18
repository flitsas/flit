using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains.RemoveDomain;

public enum RemoveDomainOutcome
{
    Retired,
    NotFound,
}

public sealed class RemoveDomainResult
{
    public required RemoveDomainOutcome Outcome { get; init; }

    public TenantDomain? Domain { get; init; }

    public static RemoveDomainResult Success(TenantDomain domain) =>
        new() { Outcome = RemoveDomainOutcome.Retired, Domain = domain };

    public static RemoveDomainResult NotFound() => new() { Outcome = RemoveDomainOutcome.NotFound };
}
