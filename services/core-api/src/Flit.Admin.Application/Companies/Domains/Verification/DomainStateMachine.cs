using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains.Verification;

/// <summary>Resultado de un lookup TXT reducido a los cuatro casos del AC7 ("coincide, ausente, valor incorrecto, desaparece").</summary>
public enum DnsCheckOutcome
{
    Matches,
    NotFound,
    Mismatch,
    Error,
}

/// <summary>Entrada PURA de una transición por comprobación DNS (HU #12425 AC2, AC4, AC7).</summary>
public sealed record DomainCheckContext(
    string CurrentStatus,
    DateTimeOffset? GraceUntil,
    int CheckAttempts,
    DnsCheckOutcome Outcome,
    DateTimeOffset Now);

/// <summary>Salida PURA de una transición: qué escribir en la fila (repositorio) y si hay que alertar (AC4).</summary>
public sealed record DomainCheckDecision(
    string NewStatus,
    string? StatusReason,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? GraceUntil,
    int CheckAttempts,
    DateTimeOffset? NextCheckAt,
    bool RaiseGraceAlert);

/// <summary>Salida PURA de la señal de certificado (HU #12425 AC3, consumida por #12426).</summary>
public sealed record DomainCertificateDecision(string NewStatus, DateTimeOffset? ActivatedAt);

/// <summary>
/// Máquina de estados del dominio (HU #12425 AC2, AC3, AC4, AC7): <c>pending → verified → active</c>;
/// cualquiera → <c>failed</c> con motivo; gracia configurable antes de fallar un dominio <c>active</c>
/// cuyo TXT desapareció. PURA — sin IO, sin reloj propio (recibe <c>now</c>), para que
/// <c>DomainStateMachineTests</c> la ejercite sin BD ni DNS real (AC7).
/// </summary>
public static class DomainStateMachine
{
    /// <summary>Transición producida por una comprobación DNS (a demanda o del job). No decide <c>active</c> (eso es <see cref="DecideCertificate"/>).</summary>
    public static DomainCheckDecision DecideCheck(DomainCheckContext context, DomainVerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        return context.CurrentStatus switch
        {
            TenantDomainStatus.Active => DecideActiveCheck(context, options),
            _ => DecideNonActiveCheck(context, options),
        };
    }

    /// <summary><c>pending</c> o <c>failed</c> — la única forma de llegar a <c>verified</c> (AC2).</summary>
    private static DomainCheckDecision DecideNonActiveCheck(DomainCheckContext context, DomainVerificationOptions options)
    {
        if (context.Outcome == DnsCheckOutcome.Matches)
        {
            // verified no se reprograma en next_check_at: el índice ix_tenant_domains_next_check no
            // incluye 'verified' — el siguiente hito es la señal de certificado (#12426), no el job.
            return new DomainCheckDecision(
                TenantDomainStatus.Verified,
                StatusReason: null,
                VerifiedAt: context.Now,
                GraceUntil: null,
                CheckAttempts: 0,
                NextCheckAt: null,
                RaiseGraceAlert: false);
        }

        var attempts = context.CheckAttempts + 1;
        return new DomainCheckDecision(
            TenantDomainStatus.Failed,
            StatusReason: ReasonFor(context.Outcome),
            VerifiedAt: null,
            GraceUntil: null,
            CheckAttempts: attempts,
            NextCheckAt: context.Now + Backoff(attempts, options),
            RaiseGraceAlert: false);
    }

    /// <summary><c>active</c> — el TXT puede desaparecer temporalmente (AC4: alerta + gracia) sin dejar de resolver.</summary>
    private static DomainCheckDecision DecideActiveCheck(DomainCheckContext context, DomainVerificationOptions options)
    {
        if (context.Outcome == DnsCheckOutcome.Matches)
        {
            return new DomainCheckDecision(
                TenantDomainStatus.Active,
                StatusReason: null,
                VerifiedAt: null,
                GraceUntil: null, // el TXT volvió: se limpia la gracia si la había (AC4)
                CheckAttempts: 0,
                NextCheckAt: context.Now + options.RevalidationInterval,
                RaiseGraceAlert: false);
        }

        var reason = ReasonFor(context.Outcome);

        if (context.GraceUntil is null)
        {
            // Primera comprobación que no encuentra el TXT en un dominio activo: entra en gracia y
            // sigue operando (AC4, "durante la gracia sigue operando").
            return new DomainCheckDecision(
                TenantDomainStatus.Active,
                StatusReason: null,
                VerifiedAt: null,
                GraceUntil: context.Now + options.GracePeriod,
                CheckAttempts: context.CheckAttempts,
                NextCheckAt: context.Now + options.InitialRetryInterval,
                RaiseGraceAlert: true);
        }

        if (context.Now < context.GraceUntil.Value)
        {
            // Ya en gracia, todavía no vence: sigue activo, reintenta más seguido.
            return new DomainCheckDecision(
                TenantDomainStatus.Active,
                StatusReason: null,
                VerifiedAt: null,
                GraceUntil: context.GraceUntil,
                CheckAttempts: context.CheckAttempts,
                NextCheckAt: context.Now + options.InitialRetryInterval,
                RaiseGraceAlert: false);
        }

        // Gracia vencida sin que el TXT volviera: pasa a failed (AC4).
        return new DomainCheckDecision(
            TenantDomainStatus.Failed,
            StatusReason: reason,
            VerifiedAt: null,
            GraceUntil: null,
            CheckAttempts: 1,
            NextCheckAt: context.Now + options.InitialRetryInterval,
            RaiseGraceAlert: false);
    }

    /// <summary>
    /// Señal de certificado emitido (#12426 → HU #12425 AC3): solo <c>verified</c> pasa a <c>active</c>
    /// (respeta <c>ck_tenant_domains_active_requires_verified</c> — nunca se activa sin haber pasado por
    /// <c>verified</c>). Si ya está <c>active</c>, solo se actualizan las fechas del certificado (sin
    /// cambio de estado) — decisión del llamador (repositorio), no de esta máquina.
    /// </summary>
    public static DomainCertificateDecision DecideCertificate(string currentStatus, DateTimeOffset now)
    {
        return currentStatus == TenantDomainStatus.Verified
            ? new DomainCertificateDecision(TenantDomainStatus.Active, now)
            : new DomainCertificateDecision(currentStatus, null);
    }

    /// <summary>Backoff exponencial acotado por <see cref="DomainVerificationOptions.MaxRetryInterval"/> (AC2 "intervalo creciente"). Público: <c>DomainStateMachineTests</c> lo ejercita directamente.</summary>
    public static TimeSpan Backoff(int attempts, DomainVerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (attempts <= 1)
        {
            return options.InitialRetryInterval;
        }

        var factor = Math.Pow(options.BackoffFactor, attempts - 1);
        var candidateTicks = options.InitialRetryInterval.Ticks * factor;
        if (candidateTicks >= options.MaxRetryInterval.Ticks || double.IsInfinity(candidateTicks))
        {
            return options.MaxRetryInterval;
        }

        return TimeSpan.FromTicks((long)candidateTicks);
    }

    private static string ReasonFor(DnsCheckOutcome outcome) => outcome switch
    {
        DnsCheckOutcome.NotFound => DomainStatusReasons.TxtNotFound,
        DnsCheckOutcome.Mismatch => DomainStatusReasons.TxtMismatch,
        DnsCheckOutcome.Error => DomainStatusReasons.DnsError,
        _ => DomainStatusReasons.DnsError,
    };
}
