using Flit.Ict.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>
/// Purga de observabilidad (HU5 / E1): borra <c>ict.integration_log</c> y <c>ict.job_runs</c> pasada
/// su retención (~90d) y conserva <c>ict.pretramite_events</c> más tiempo (~1a, es el timeline de
/// negocio). A diferencia de <see cref="IctPollingJob"/> corre 24/7 SIN la ventana 08-20 (la purga es
/// mantenimiento, no pipeline) con cadencia en horas. Borra por LOTES (ctid + LIMIT) para no tomar
/// locks largos ni inflar el WAL. Cross-tenant: NO fija el GUC de tenant — el rol owner ve todas las
/// filas (integration_log/job_runs sin RLS; pretramite_events con RLS ENABLE no-FORCE).
/// Bug #13304: además vacía <c>vehicle_snapshot</c> (PII) de las respuestas con más de 2× la vigencia RUNT.
/// </summary>
public sealed class RetentionJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    ILogger<RetentionJob> logger) : BackgroundService
{
    private IctJobOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.RetentionEnabled)
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
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // un ciclo de purga fallido no debe tumbar el job
            catch (Exception ex)
            {
                RetentionJobLog.SweepError(logger, ex);
            }
#pragma warning restore CA1031

            try
            {
                await Task.Delay(TimeSpan.FromHours(Options.RetentionIntervalHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();

        // Bug #13304 (H-1): primero el snapshot RUNT (PII), para que un fallo de las purgas siguientes no lo retrase.
        var snapshots = await ExecuteInBatchesAsync(
            db, VehicleSnapshotSweepSql, () => VehicleSnapshotSweepParameters(Options), ct).ConfigureAwait(false);
        var logs = await PurgeTableAsync(db, "ict.integration_log", Options.IntegrationLogRetentionDays, ct)
            .ConfigureAwait(false);
        var events = await PurgeTableAsync(db, "ict.pretramite_events", Options.PretramiteEventsRetentionDays, ct)
            .ConfigureAwait(false);
        var runs = await PurgeTableAsync(db, "ict.job_runs", Options.JobRunsRetentionDays, ct)
            .ConfigureAwait(false);

        RetentionJobLog.SweepDone(logger, logs, events, runs, snapshots);
    }

    /// <summary>
    /// Bug #13304 (H-1) — barrido por lotes del resultado completo de la consulta RUNT (@pii:high). Vacía la
    /// columna (la respuesta queda para la trazabilidad) en las filas con más de 2× la vigencia: cubre una
    /// purga fallida tras ps=4/5/6 y los snapshots de consultas que terminaron después de una novedad.
    /// </summary>
    internal const string VehicleSnapshotSweepSql = """
        UPDATE ict.external_integration_source_response
        SET vehicle_snapshot = NULL
        WHERE ctid IN (
            SELECT ctid FROM ict.external_integration_source_response
            WHERE vehicle_snapshot IS NOT NULL
              AND created_at < now() - make_interval(hours => @hours)
            LIMIT @batch
        )
        """;

    /// <summary>Antigüedad (horas) a partir de la cual se vacía el snapshot: 2× la vigencia (≤ 0 ⇒ 24 h).</summary>
    internal static int VehicleSnapshotRetentionHours(IctJobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return 2 * SendToCoreApiJob.VigenciaConsultaRuntHoras(options.VehicleConsultationMaxAgeHours);
    }

    /// <summary>Parámetros del barrido; instancias nuevas por lote (un NpgsqlParameter no se reutiliza).</summary>
    internal static object[] VehicleSnapshotSweepParameters(IctJobOptions options) =>
    [
        new Npgsql.NpgsqlParameter("hours", VehicleSnapshotRetentionHours(options)),
        new Npgsql.NpgsqlParameter("batch", options.RetentionBatchSize),
    ];

    /// <summary>
    /// Borra por lotes las filas de <paramref name="table"/> con <c>created_at</c> anterior a la
    /// retención. <paramref name="table"/> es una constante interna (no input de usuario); los días y
    /// el tamaño de lote van parametrizados.
    /// </summary>
    private async Task<int> PurgeTableAsync(IctDbContext db, string table, int retentionDays, CancellationToken ct)
    {
        var sql = $"""
            DELETE FROM {table}
            WHERE ctid IN (
                SELECT ctid FROM {table}
                WHERE created_at < now() - make_interval(days => @days)
                LIMIT @batch
            )
            """;

        return await ExecuteInBatchesAsync(
            db,
            sql,
            () =>
            [
                new Npgsql.NpgsqlParameter("days", retentionDays),
                new Npgsql.NpgsqlParameter("batch", Options.RetentionBatchSize),
            ],
            ct).ConfigureAwait(false);
    }

    /// <summary>Repite <paramref name="sql"/> (que afecta a lo sumo un lote) hasta que un lote quede incompleto.</summary>
    private async Task<int> ExecuteInBatchesAsync(
        IctDbContext db, string sql, Func<object[]> parameters, CancellationToken ct)
    {
        var total = 0;
        int affected;
        do
        {
            affected = await db.Database.ExecuteSqlRawAsync(sql, parameters(), ct).ConfigureAwait(false);
            total += affected;
        }
        while (affected >= Options.RetentionBatchSize && !ct.IsCancellationRequested);

        return total;
    }
}

internal static partial class RetentionJobLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "ICT retención: purgados {Logs} logs, {Events} eventos, {Runs} corridas de job; {Snapshots} snapshots RUNT vaciados.")]
    public static partial void SweepDone(ILogger logger, int logs, int events, int runs, int snapshots);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "ICT retención: ciclo de purga fallido; se reintenta en el siguiente intervalo.")]
    public static partial void SweepError(ILogger logger, Exception ex);
}
