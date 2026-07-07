using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Telemetry;

/// <summary>
/// Writer asíncrono de la telemetría de uso (HU-A Reportes 2.0). Drena la cola en memoria
/// (<see cref="ChannelUsageEventQueue"/>) en lotes de hasta <see cref="MaxBatchSize"/> eventos
/// (o cada <see cref="FlushInterval"/>) e inserta en <c>analytics.app_usage_events</c> con el
/// <see cref="FlitDbContext"/> (AddRange + SaveChanges). Mismo patrón operativo que
/// <c>IdentityValidationOutboxProcessor</c>: scopeFactory, startup delay, loop con try/catch y
/// LoggerMessage. Además aplica la retención: una vez al día borra los eventos con
/// <c>occurred_at &lt; now() - RetentionDays</c>. Ningún fallo aquí afecta a los requests:
/// la telemetría es best-effort (log + reintento en el siguiente ciclo).
/// </summary>
public sealed class UsageEventWriterProcessor(
    ChannelUsageEventQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<AnalyticsTelemetryOptions> options,
    ILogger<UsageEventWriterProcessor> logger) : BackgroundService
{
    internal const int MaxBatchSize = 200;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

    /// <summary>Tamaño de página del borrado de retención (memoria acotada, compatible InMemory).</summary>
    private const int CleanupPageSize = 5_000;

    private DateTimeOffset _nextCleanupUtc = DateTimeOffset.UtcNow + CleanupInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeño retraso inicial: deja terminar migraciones/seed del arranque antes de escribir.
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Espera a que haya eventos O a que venza el intervalo de flush (lo que ocurra
                // primero): así los lotes se forman naturalmente y el loop itera al menos cada
                // FlushInterval para evaluar la limpieza de retención.
                var wait = queue.WaitToReadAsync(stoppingToken).AsTask();
                await Task.WhenAny(wait, Task.Delay(FlushInterval, stoppingToken));

                await FlushPendingAsync(stoppingToken);

                if (DateTimeOffset.UtcNow >= _nextCleanupUtc)
                {
                    _nextCleanupUtc = DateTimeOffset.UtcNow + CleanupInterval;
                    var purged = await CleanupRetentionAsync(stoppingToken);
                    if (purged > 0)
                        UsageWriterLog.RetentionPurged(logger, purged, options.Value.RetentionDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                UsageWriterLog.CycleError(logger, ex);
                try { await Task.Delay(FlushInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>
    /// Drena hasta <see cref="MaxBatchSize"/> eventos de la cola y los inserta en un único
    /// SaveChanges. Devuelve cuántos persistió. Si la inserción falla, los eventos del lote se
    /// pierden (telemetría best-effort: no se reencolan para no formar venenos).
    /// </summary>
    internal async Task<int> FlushPendingAsync(CancellationToken ct)
    {
        var batch = new List<AppUsageEvent>(capacity: 16);
        while (batch.Count < MaxBatchSize && queue.TryDequeue(out var evt))
            batch.Add(ToEntity(evt));

        if (batch.Count == 0)
            return 0;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        db.Set<AppUsageEvent>().AddRange(batch);
        await db.SaveChangesAsync(ct);
        UsageWriterLog.BatchWritten(logger, batch.Count);
        return batch.Count;
    }

    /// <summary>
    /// Retención: borra los eventos con <c>occurred_at</c> más viejos que
    /// <see cref="AnalyticsTelemetryOptions.RetentionDays"/>, por páginas (memoria acotada y
    /// compatible con el provider InMemory de los tests). Devuelve el total borrado.
    /// </summary>
    internal async Task<int> CleanupRetentionAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.RetentionDays);
        var total = 0;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

        while (!ct.IsCancellationRequested)
        {
            var page = await db.Set<AppUsageEvent>()
                .Where(e => e.OccurredAt < cutoff)
                .OrderBy(e => e.OccurredAt)
                .Take(CleanupPageSize)
                .ToListAsync(ct);
            if (page.Count == 0)
                break;

            db.RemoveRange(page);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += page.Count;
        }

        return total;
    }

    private static AppUsageEvent ToEntity(UsageEventRecord evt) => new()
    {
        // Id lo genera la BD (uuidv7()) o el value generator del provider de tests.
        TenantId = evt.TenantId,
        UserId = evt.UserId,
        EventType = evt.EventType,
        Module = evt.Module,
        StepKey = evt.StepKey,
        ProcedureInstanceId = evt.ProcedureInstanceId,
        DurationMs = evt.DurationMs is < 0 ? null : evt.DurationMs,
        Metadata = string.IsNullOrWhiteSpace(evt.MetadataJson) ? "{}" : evt.MetadataJson,
        OccurredAt = evt.OccurredAt,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}

/// <summary>Logging source-generated (CA1848) del writer de telemetría.</summary>
internal static partial class UsageWriterLog
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Telemetría de uso: lote de {Count} evento(s) persistido en analytics.app_usage_events.")]
    public static partial void BatchWritten(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Telemetría de uso: retención aplicada — {Count} evento(s) más viejos que {RetentionDays} días eliminados.")]
    public static partial void RetentionPurged(ILogger logger, int count, int retentionDays);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Telemetría de uso: error en el ciclo del writer; se reintentará (los requests no se ven afectados).")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
