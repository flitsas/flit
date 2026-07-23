using Flit.Ict.Domain.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>Opciones de los jobs programados (ventana horaria y cadencias). Migra las 5 lambdas v1.</summary>
public sealed class IctJobOptions
{
    public const string SectionName = "Jobs";

    public bool Enabled { get; init; } = true;

    public int WindowStartHour { get; init; } = 8;

    public int WindowEndHour { get; init; } = 20;

    public int StartupDelaySeconds { get; init; } = 10;

    public int BusinessPollSeconds { get; init; } = 45;

    public int ExternalPollSeconds { get; init; } = 45;

    public int OrchestratorPollSeconds { get; init; } = 20;

    public int SendPollSeconds { get; init; } = 20;

    public int WebhookPollSeconds { get; init; } = 10;
}

/// <summary>
/// Base de los jobs de core-ict: patrón polling in-process (StartupDelay + PollInterval) con ventana
/// horaria configurable y try/catch por ciclo, calcado del AnalyticsSchedulerProcessor de core-api.
/// Mejora sobre v1: latencia de segundos-decenas en vez de 9-19 min (más señal event-driven futura).
/// </summary>
public abstract class IctPollingJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    ILogger logger) : BackgroundService
{
    protected IctJobOptions Options => options.Value;

    protected abstract TimeSpan PollInterval { get; }

    protected abstract string JobName { get; }

    protected abstract Task RunCycleAsync(IServiceScope scope, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Options.StartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IctWindowEvaluator.IsWithinWindow(DateTime.UtcNow, Options.WindowStartHour, Options.WindowEndHour))
                {
                    using var scope = scopeFactory.CreateScope();
                    await RunCycleAsync(scope, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // un ciclo fallido no debe tumbar el job
            catch (Exception ex)
            {
                IctJobLog.CycleError(logger, ex, JobName);
            }
#pragma warning restore CA1031

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

internal static partial class IctJobLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "ICT job {JobName}: ciclo fallido; se reintenta en el siguiente intervalo.")]
    public static partial void CycleError(ILogger logger, Exception ex, string jobName);

    [LoggerMessage(Level = LogLevel.Information, Message = "ICT job {JobName}: {Count} elementos procesados.")]
    public static partial void CycleDone(ILogger logger, string jobName, int count);
}
