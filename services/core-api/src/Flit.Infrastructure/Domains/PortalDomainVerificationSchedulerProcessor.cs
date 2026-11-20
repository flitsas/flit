using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Application.Companies.TransitOffices.PortalDominio;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Domains;

/// <summary>
/// HU #14382 — el trabajo programado de la comprobación de titularidad DNS para los dominios de portal de organismo. Es el mismo
/// ciclo que <see cref="DomainVerificationSchedulerProcessor"/> (mismas opciones <c>Domains:Verification</c>, mismo reclamo
/// <c>FOR UPDATE SKIP LOCKED</c>, mismo aislamiento por fila), pero sobre <c>admin.transit_office_domains</c> y con
/// <see cref="PortalDominioVerificador"/>. El de marca blanca no se toca.
/// </summary>
internal sealed partial class PortalDomainVerificationSchedulerProcessor(
    IServiceScopeFactory scopeFactory,
    DomainVerificationOptions options,
    ILogger<PortalDomainVerificationSchedulerProcessor> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    public const string JobActor = "job:dns-verification-portal";

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(25);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
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
                CicloFallido(logger, ex);
            }

            try { await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>UN ciclo de reclamo y comprobación. Sin dominios vencidos no toca ninguna fila.</summary>
    internal async Task<int> TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPortalDominioRepository>();
        var verificador = scope.ServiceProvider.GetRequiredService<PortalDominioVerificador>();

        var arriendo = TimeSpan.FromMinutes(Math.Max(2, options.PollInterval.TotalMinutes * 4));
        var reclamos = await repo.ReclamarVencidosAsync(options.BatchSize, arriendo, _clock.GetUtcNow(), ct).ConfigureAwait(false);

        var procesados = 0;
        foreach (var reclamo in reclamos)
        {
            try
            {
                await verificador.ProcesarReclamoAsync(reclamo, JobActor, ct).ConfigureAwait(false);
                procesados++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Aislamiento por fila: un dominio con error de DNS o de BD no tumba el lote; se reintenta al vencer el arriendo.
                ReclamoFallido(logger, reclamo.TransitOfficeId, reclamo.Host, ex);
            }
        }

        return procesados;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Comprobación DNS de dominios de portal: el ciclo falló; se reintenta en el siguiente sondeo.")]
    private static partial void CicloFallido(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Comprobación DNS del dominio de portal {TransitOfficeId} ({Host}) falló; se reintentará en un ciclo posterior.")]
    private static partial void ReclamoFallido(ILogger logger, Guid transitOfficeId, string host, Exception ex);
}
