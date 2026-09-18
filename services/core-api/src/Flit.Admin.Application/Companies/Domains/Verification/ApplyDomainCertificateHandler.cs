using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains.Verification;

public enum ApplyDomainCertificateOutcome
{
    Activated,
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
/// por <c>PUT /internal/domains/{host}/certificate</c>. Contrato §3: <c>verified</c> pasa a
/// <c>active</c> (<c>activated_at = now()</c>, AC3); <c>active</c> solo actualiza fechas del
/// certificado; cualquier otro estado (<c>pending</c>/<c>failed</c>) es <c>409 DOMAIN_NOT_VERIFIED</c>
/// — no tiene sentido emitir certificado para un dominio que no comprobó titularidad.
/// </summary>
public sealed class ApplyDomainCertificateHandler(ITenantDomainRepository repository, TimeProvider? timeProvider = null)
{
    private readonly ITenantDomainRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

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

        var now = _clock.GetUtcNow();
        var decision = DomainStateMachine.DecideCertificate(current.Status, now);

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

        return ApplyDomainCertificateResult.Ok(
            updated,
            decision.NewStatus == TenantDomainStatus.Active && current.Status == TenantDomainStatus.Verified
                ? ApplyDomainCertificateOutcome.Activated
                : ApplyDomainCertificateOutcome.Updated);
    }
}
