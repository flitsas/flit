using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains;

/// <summary>Instrucciones DNS del contrato (§3 #12416, #12427): TXT de comprobación (#12425) y CNAME hacia el borde (#12421).</summary>
public sealed record DomainVerificationResponse(string TxtName, string TxtValue, string CnameName, string CnameTarget);

public sealed record DomainCertificateResponse(DateTimeOffset? IssuedAt, DateTimeOffset? ExpiresAt);

/// <summary>Forma de <c>GET/PUT .../domain</c> (SuperAdmin y autogestión, HU #12416, contrato §3/§4).</summary>
public sealed record TenantDomainResponse(
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
    public static TenantDomainResponse From(TenantDomain domain, string edgeTarget)
    {
        ArgumentNullException.ThrowIfNull(domain);

        return new TenantDomainResponse(
            domain.Host,
            domain.Status,
            domain.StatusReason,
            domain.StatusChangedAt,
            new DomainVerificationResponse(
                $"_flit-verify.{domain.Host}",
                domain.VerificationToken,
                domain.Host,
                edgeTarget),
            domain.VerifiedAt,
            domain.ActivatedAt,
            new DomainCertificateResponse(domain.CertificateIssuedAt, domain.CertificateExpiresAt),
            domain.LastCheckedAt,
            domain.NextCheckAt,
            domain.GraceUntil,
            domain.RowVersion);
    }
}
