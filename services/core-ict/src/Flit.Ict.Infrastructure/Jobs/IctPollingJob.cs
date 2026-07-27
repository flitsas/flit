using System.Diagnostics;
using Flit.Ict.Domain.Jobs;
using Flit.Ict.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

    // Retención/purga (HU5): corre 24/7 (sin ventana horaria), cadencia en horas.
    public bool RetentionEnabled { get; init; } = true;

    public int IntegrationLogRetentionDays { get; init; } = 90;

    public int PretramiteEventsRetentionDays { get; init; } = 365;

    public int JobRunsRetentionDays { get; init; } = 90;

    public int RetentionIntervalHours { get; init; } = 24;

    public int RetentionBatchSize { get; init; } = 5000;
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
                    var startedAt = DateTime.UtcNow;
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        await RunCycleAsync(scope, stoppingToken);
                        stopwatch.Stop();
                        await RecordRunAsync(startedAt, stopwatch.Elapsed, "ok", null, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        stopwatch.Stop();
                        // Registrar la corrida fallida con un token no cancelado (el ciclo ya se abortó);
                        // luego relanzar para que el catch externo la loguee (CycleError).
                        await RecordRunAsync(startedAt, stopwatch.Elapsed, "error", ex.Message, CancellationToken.None);
                        throw;
                    }
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

    /// <summary>
    /// Registra el ciclo en <c>ict.job_runs</c> (auditoría + SLA de la métrica ict_jobs_out_of_sla).
    /// Best-effort en scope propio: un fallo de auditoría NUNCA rompe el job.
    /// SLA incumplido = el ciclo falló, o su duración superó su propio <see cref="PollInterval"/>
    /// (el job no alcanza a seguir su cadencia → backpressure).
    /// </summary>
    private async Task RecordRunAsync(
        DateTime startedAt, TimeSpan duration, string outcome, string? error, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();

            var durationMs = (int)Math.Min(duration.TotalMilliseconds, int.MaxValue);
            var breachedSla = outcome == "error" || duration > PollInterval;
            var finishedAt = startedAt.Add(duration);
            var errorMessage = error is { Length: > 2000 } ? error[..2000] : error;

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ict.job_runs
                    (job_name, started_at, finished_at, duration_ms, outcome, breached_sla, error_message)
                VALUES
                    ({JobName}, {startedAt}, {finishedAt}, {durationMs}, {outcome}, {breachedSla}, {errorMessage})
                """,
                ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // la auditoría de jobs nunca debe romper el ciclo
        catch (Exception ex)
        {
            IctJobLog.JobRunRecordError(logger, ex, JobName);
        }
#pragma warning restore CA1031
    }
}

internal static partial class IctJobLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "ICT job {JobName}: ciclo fallido; se reintenta en el siguiente intervalo.")]
    public static partial void CycleError(ILogger logger, Exception ex, string jobName);

    [LoggerMessage(Level = LogLevel.Information, Message = "ICT job {JobName}: {Count} elementos procesados.")]
    public static partial void CycleDone(ILogger logger, string jobName, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ICT job {JobName}: no se pudo registrar la corrida en job_runs (se ignora).")]
    public static partial void JobRunRecordError(ILogger logger, Exception ex, string jobName);
}
