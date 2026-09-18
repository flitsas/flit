using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Domains;

/// <summary>
/// El "cron" de la comprobación de titularidad DNS (HU #12425 AC2, AC4, AC6, ADR-0059: BackgroundService
/// dentro de core-api, sin cron/broker externo). Cada <see cref="DomainVerificationOptions.PollInterval"/>
/// reclama hasta <see cref="DomainVerificationOptions.BatchSize"/> filas vencidas
/// (<c>ClaimDueForCheckAsync</c>, <c>FOR UPDATE SKIP LOCKED</c> sobre <c>ix_tenant_domains_next_check</c>)
/// y, para cada una, consulta el TXT y aplica <see cref="DomainStateMachine"/> — MISMA lógica que
/// <see cref="VerifyDomainHandler"/> (a demanda), sin cooldown, con <c>ChangedBy = "job:dns-verification"</c>
/// (AC5). Sin dominios vencidos, el ciclo no toca ninguna fila (AC6, "el trabajo programado no hace
/// nada"). Un fallo en UNA fila (DNS caído, excepción) no aborta el lote: se registra y se continúa con
/// la siguiente (aislamiento por fila, patrón <c>IdentityValidationOutboxProcessor</c>).
/// </summary>
internal sealed class DomainVerificationSchedulerProcessor(
    IServiceScopeFactory scopeFactory,
    DomainVerificationOptions options,
    ILogger<DomainVerificationSchedulerProcessor> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    public const string JobActor = "job:dns-verification";

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            SchedulerLog.Disabled(logger);
            return;
        }

        try { await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SchedulerLog.CycleError(logger, ex);
            }

            try { await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>UN ciclo de reclamo + verificación (AC6: sin filas vencidas, no hace nada). Público para el test del "cron" sin esperar el timer real.</summary>
    internal async Task<int> TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITenantDomainRepository>();

        var lease = TimeSpan.FromMinutes(Math.Max(2, options.PollInterval.TotalMinutes * 4));
        var claims = await repository.ClaimDueForCheckAsync(options.BatchSize, lease, _clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (claims.Count == 0)
        {
            return 0;
        }

        var processed = 0;
        foreach (var claim in claims)
        {
            try
            {
                await ProcessClaimAsync(scope.ServiceProvider, claim, ct).ConfigureAwait(false);
                processed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Aislamiento por fila (AC6/AC7): un dominio con error de DNS/BD no tumba el lote — la
                // fila conserva el next_check_at "leased" y se reintentará en un ciclo posterior.
                SchedulerLog.ClaimError(logger, claim.TenantId, claim.Host, ex);
            }
        }

        return processed;
    }

    private async Task ProcessClaimAsync(IServiceProvider services, DomainCheckClaim claim, CancellationToken ct)
    {
        var dnsResolver = services.GetRequiredService<IDnsTxtResolver>();
        var repository = services.GetRequiredService<ITenantDomainRepository>();

        var domain = await repository.GetByTenantIdAsync(claim.TenantId, ct).ConfigureAwait(false);
        if (domain is null)
        {
            return; // retirado entre el claim y el proceso — nada que hacer.
        }

        var lookup = await dnsResolver.LookupAsync(domain.Host, ct).ConfigureAwait(false);
        var outcome = ToOutcome(lookup, domain.VerificationToken);
        var now = _clock.GetUtcNow();

        var decision = DomainStateMachine.DecideCheck(
            new DomainCheckContext(domain.Status, domain.GraceUntil, domain.CheckAttempts, outcome, now),
            options);

        if (decision.RaiseGraceAlert)
        {
            SchedulerLog.GraceAlert(logger, claim.TenantId, domain.Host, decision.GraceUntil!.Value);
        }

        await repository.ApplyCheckOutcomeAsync(
            claim.TenantId,
            decision.NewStatus,
            decision.StatusReason,
            decision.VerifiedAt,
            decision.GraceUntil,
            decision.CheckAttempts,
            decision.NextCheckAt,
            now,
            changedByUserId: null,
            changedByJob: JobActor,
            ct).ConfigureAwait(false);
    }

    private static DnsCheckOutcome ToOutcome(DnsTxtLookupResult lookup, string expectedToken)
    {
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

    private static string NormalizeTxtValue(string rawValue)
    {
        const string prefix = "flit-verify=";
        var trimmed = rawValue.Trim().Trim('"');
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? trimmed[prefix.Length..] : trimmed;
    }
}

/// <summary>Logging source-generado (CA1848) del job de comprobación DNS (HU #12425).</summary>
internal static partial class SchedulerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Comprobación DNS de dominios: deshabilitada (Domains:Verification:Enabled=false).")]
    public static partial void Disabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Comprobación DNS de dominios: el ciclo del programador falló; se reintenta en el siguiente sondeo.")]
    public static partial void CycleError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Comprobación DNS del dominio {TenantId} ({Host}) falló; se reintentará en un ciclo posterior.")]
    public static partial void ClaimError(ILogger logger, Guid tenantId, string host, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dominio activo {TenantId} ({Host}) perdió su registro TXT; entra en gracia hasta {GraceUntil} (HU #12425 AC4).")]
    public static partial void GraceAlert(ILogger logger, Guid tenantId, string host, DateTimeOffset graceUntil);
}
