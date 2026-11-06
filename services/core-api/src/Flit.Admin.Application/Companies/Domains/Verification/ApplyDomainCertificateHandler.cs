using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains.Verification;

public enum ApplyDomainCertificateOutcome
{
    Updated,
    NotFound,
    NotVerified,
}

public sealed record ApplyDomainCertificateResult(ApplyDomainCertificateOutcome Outcome, TenantDomain? Domain)
{
    public static ApplyDomainCertificateResult Ok(TenantDomain domain, ApplyDomainCertificateOutcome outcome) => new(outcome, domain);

    public static ApplyDomainCertificateResult NotFound() => new(ApplyDomainCertificateOutcome.NotFound, null);

    public static ApplyDomainCertificateResult NotVerified() => new(ApplyDomainCertificateOutcome.NotVerified, null);
}

/// <summary>
/// Aplica la señal de certificado del borde (#12426) sobre el ciclo de estados de #12425 — consumido
/// por <c>PUT /internal/domains/{host}/certificate</c>. <c>verified</c> y <c>active</c> solo actualizan
/// las fechas del certificado (la activación la pide el SuperAdmin); cualquier otro estado
/// (<c>pending</c>/<c>failed</c>) es <c>409 DOMAIN_NOT_VERIFIED</c> — no tiene sentido emitir
/// certificado para un dominio que no comprobó titularidad.
/// </summary>
public sealed class ApplyDomainCertificateHandler(ITenantDomainRepository repository)
{
    private readonly ITenantDomainRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public const string JobActor = "job:certificate-issuer";

    public async Task<ApplyDomainCertificateResult> HandleAsync(
        string host, DateTimeOffset issuedAt, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var current = await _repository.GetByHostAsync(host, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return ApplyDomainCertificateResult.NotFound();
        }

        if (current.Status is not (TenantDomainStatus.Verified or TenantDomainStatus.Active))
        {
            return ApplyDomainCertificateResult.NotVerified();
        }

        var decision = DomainStateMachine.DecideCertificate(current.Status);

        var updated = await _repository.ApplyCertificateAsync(
            host,
            decision.NewStatus,
            decision.ActivatedAt,
            issuedAt,
            expiresAt,
            JobActor,
            cancellationToken).ConfigureAwait(false);

        if (updated is null)
        {
            return ApplyDomainCertificateResult.NotFound();
        }

        return ApplyDomainCertificateResult.Ok(updated, ApplyDomainCertificateOutcome.Updated);
    }
}
