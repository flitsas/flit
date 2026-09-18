using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains.RegisterDomain;

public enum RegisterDomainOutcome
{
    Registered,
    Invalid,
    Conflict,
    TenantNotMarcaBlanca,
    HostAlreadyRegistered,
    AlreadyRegisteredForTenant,
}

/// <summary>Resultado de <see cref="RegisterDomainHandler"/> (HU #12416 AC1, AC2, AC3, AC5).</summary>
public sealed class RegisterDomainResult
{
    public required RegisterDomainOutcome Outcome { get; init; }

    public TenantDomain? Domain { get; init; }

    public string? ErrorCode { get; init; }

    public static RegisterDomainResult Success(TenantDomain domain) =>
        new() { Outcome = RegisterDomainOutcome.Registered, Domain = domain };

    public static RegisterDomainResult Invalid(string errorCode) =>
        new() { Outcome = RegisterDomainOutcome.Invalid, ErrorCode = errorCode };

    public static RegisterDomainResult Conflict() =>
        new() { Outcome = RegisterDomainOutcome.Conflict, ErrorCode = DomainErrors.ConcurrencyConflict };

    public static RegisterDomainResult TenantNotMarcaBlanca() =>
        new() { Outcome = RegisterDomainOutcome.TenantNotMarcaBlanca, ErrorCode = DomainErrors.TenantNotMarcaBlanca };

    public static RegisterDomainResult HostAlreadyRegistered() =>
        new() { Outcome = RegisterDomainOutcome.HostAlreadyRegistered, ErrorCode = DomainErrors.HostAlreadyRegistered };

    public static RegisterDomainResult AlreadyRegisteredForTenant() =>
        new() { Outcome = RegisterDomainOutcome.AlreadyRegisteredForTenant, ErrorCode = DomainErrors.AlreadyRegisteredForTenant };
}
