using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains;

/// <summary>Instrucciones DNS del contrato (§3 #12416, #12427): TXT de comprobación (#12425) y registro A hacia el borde.</summary>
public sealed record DomainVerificationResponse(string TxtName, string TxtValue, string AName, string AValue);

public sealed record DomainCertificateResponse(DateTimeOffset? IssuedAt, DateTimeOffset? ExpiresAt);

/// <summary>Forma de <c>GET/PUT .../domain</c> (SuperAdmin y autogestión, HU #12416, contrato §3/§4).</summary>
public sealed record TenantDomainResponse(
    string Purpose,
    string Host,
    string Status,
    string? StatusReason,
    DateTimeOffset StatusChangedAt,
    DomainVerificationResponse Verification,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? ActivatedAt,
    DomainCertificateResponse Certificate,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? NextCheckAt,
    DateTimeOffset? GraceUntil,
    long RowVersion)
{
    public static TenantDomainResponse From(TenantDomain domain, string edgeIp)
    {
        ArgumentNullException.ThrowIfNull(domain);

        return new TenantDomainResponse(
            domain.Purpose,
            domain.Host,
            domain.Status,
            domain.StatusReason,
            domain.StatusChangedAt,
            new DomainVerificationResponse(
                $"_flit-verify.{domain.Host}",
                domain.VerificationToken,
                domain.Host,
                edgeIp),
            domain.VerifiedAt,
            domain.ActivatedAt,
            new DomainCertificateResponse(domain.CertificateIssuedAt, domain.CertificateExpiresAt),
            domain.LastCheckedAt,
            domain.NextCheckAt,
            domain.GraceUntil,
            domain.RowVersion);
    }
}

/// <summary>Forma de <c>GET .../domains</c>: los dominios de la red y lo que falta para activarla.</summary>
public sealed record NetworkDomainsResponse(
    IReadOnlyList<TenantDomainResponse> Domains,
    IReadOnlyList<string> Products,
    NetworkActivationResponse Activation);

/// <summary>Propósitos sin dominio listo, y si la red se puede activar o ya está activa.</summary>
public sealed record NetworkActivationResponse(bool Ready, bool Active, IReadOnlyList<string> Pending);
