using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains.Verification;

public enum VerifyDomainOutcome
{
    Verified,
    Failed,
    NotFound,
    Cooldown,
}

/// <summary>Resultado de una comprobación DNS a demanda (HU #12425 AC2) — mismo tipo para el endpoint SuperAdmin y el de autogestión.</summary>
public sealed record VerifyDomainResult(VerifyDomainOutcome Outcome, TenantDomain? Domain, int? RetryAfterSeconds)
{
    public static VerifyDomainResult Ok(TenantDomain domain, VerifyDomainOutcome outcome) => new(outcome, domain, null);

    public static VerifyDomainResult NotFound() => new(VerifyDomainOutcome.NotFound, null, null);

    public static VerifyDomainResult TooSoon(int retryAfterSeconds) => new(VerifyDomainOutcome.Cooldown, null, retryAfterSeconds);
}

/// <summary>
/// Comprobación DNS a demanda de un dominio (HU #12425 AC2, consumido por
/// <c>POST /admin/companies/{tenantId}/domain/verify</c> y <c>POST /company/domain/verify</c>). El
/// mismo camino (<see cref="DomainStateMachine.DecideCheck"/> + <c>ITenantDomainRepository.ApplyCheckOutcomeAsync</c>)
/// que usa <c>DomainVerificationSchedulerProcessor</c> para el trabajo programado — la única diferencia
/// es el enfriamiento (<see cref="DomainVerificationOptions.ManualCooldownSeconds"/>, AC del contrato
/// §3 "<c>429</c> si se supera") y quién queda como autor de la auditoría (AC5: usuario vs. job).
/// </summary>
public sealed class VerifyDomainHandler(
    ITenantDomainRepository repository,
    IDnsTxtResolver dnsResolver,
    DomainVerificationOptions options,
    TimeProvider? timeProvider = null)
{
    private readonly ITenantDomainRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IDnsTxtResolver _dnsResolver = dnsResolver ?? throw new ArgumentNullException(nameof(dnsResolver));
    private readonly DomainVerificationOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<VerifyDomainResult> HandleAsync(Guid tenantId, Guid? changedByUserId, CancellationToken cancellationToken = default)
    {
        var current = await _repository.GetByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return VerifyDomainResult.NotFound();
        }

        var now = _clock.GetUtcNow();
        if (current.LastCheckedAt is { } lastChecked)
        {
            var elapsed = now - lastChecked;
            var cooldown = TimeSpan.FromSeconds(_options.ManualCooldownSeconds);
            if (elapsed < cooldown)
            {
                return VerifyDomainResult.TooSoon((int)Math.Ceiling((cooldown - elapsed).TotalSeconds));
            }
        }

        var outcome = await CheckAsync(current.Host, current.VerificationToken, cancellationToken).ConfigureAwait(false);
        var decision = DomainStateMachine.DecideCheck(
            new DomainCheckContext(current.Status, current.GraceUntil, current.CheckAttempts, outcome, now),
            _options);

        var updated = await _repository.ApplyCheckOutcomeAsync(
            tenantId,
            decision.NewStatus,
            decision.StatusReason,
            decision.VerifiedAt,
            decision.GraceUntil,
            decision.CheckAttempts,
            decision.NextCheckAt,
            now,
            changedByUserId,
            changedByJob: null,
            cancellationToken).ConfigureAwait(false);

        if (updated is null)
        {
            return VerifyDomainResult.NotFound();
        }

        return VerifyDomainResult.Ok(
            updated,
            decision.NewStatus == TenantDomainStatus.Verified || (decision.NewStatus == TenantDomainStatus.Active)
                ? VerifyDomainOutcome.Verified
                : VerifyDomainOutcome.Failed);
    }

    /// <summary>Consulta el TXT y lo reduce a los 4 casos de <see cref="DnsCheckOutcome"/> (AC7). Nunca loguea el valor crudo del TXT (auditoría DnsClient).</summary>
    internal async Task<DnsCheckOutcome> CheckAsync(string host, string expectedToken, CancellationToken cancellationToken)
    {
        var lookup = await _dnsResolver.LookupAsync(host, cancellationToken).ConfigureAwait(false);
        if (lookup.Error is not null)
        {
            return DnsCheckOutcome.Error;
        }

        if (!lookup.Found || lookup.Values.Count == 0)
        {
            return DnsCheckOutcome.NotFound;
        }

        return lookup.Values.Any(v => string.Equals(NormalizeTxtValue(v), expectedToken, StringComparison.Ordinal))
            ? DnsCheckOutcome.Matches
            : DnsCheckOutcome.Mismatch;
    }

    /// <summary>El registro TXT suele publicarse como <c>flit-verify=&lt;token&gt;</c>; también se acepta el token desnudo.</summary>
    private static string NormalizeTxtValue(string rawValue)
    {
        const string prefix = "flit-verify=";
        var trimmed = rawValue.Trim().Trim('"');
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;
    }
}
