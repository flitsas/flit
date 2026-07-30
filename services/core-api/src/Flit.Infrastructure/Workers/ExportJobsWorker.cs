using System.Text;
using System.Text.Json;
using Flit.Analytics.Application.Reporting;
using Flit.Infrastructure.Hubs;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Flit.Infrastructure.Workers;

/// <summary>
/// Escucha NOTIFY 'export_jobs_channel' y despierta al worker.
/// Fallback: el worker también hace polling cada 30 s (ADR-0037).
/// </summary>
internal sealed partial class ExportJobsChannelListener(
    IConfiguration configuration,
    ILogger<ExportJobsChannelListener> logger) : BackgroundService
{
    private readonly IConfiguration _configuration = configuration;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cs = _configuration.GetConnectionString("FlitDb")
            ?? _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
        {
            LogNoConnectionString(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(cs);
                await conn.OpenAsync(stoppingToken);
                conn.Notification += (_, e) =>
                {
                    if (string.Equals(e.Channel, "export_jobs_channel", StringComparison.Ordinal))
                        LogNotifyReceived(logger, e.Payload);
                };
                await using (var cmd = new NpgsqlCommand("LISTEN export_jobs_channel", conn))
                    await cmd.ExecuteNonQueryAsync(stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                    await conn.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogListenerRestart(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ExportJobsChannelListener: sin connection string; deshabilitado")]
    private static partial void LogNoConnectionString(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "NOTIFY export_jobs_channel: {Payload}")]
    private static partial void LogNotifyReceived(ILogger logger, string payload);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ExportJobsChannelListener reiniciando en 5 s")]
    private static partial void LogListenerRestart(ILogger logger, Exception ex);
}

/// <summary>
/// Procesa export_jobs pendientes con FOR UPDATE SKIP LOCKED (ADR-0037).
/// </summary>
internal sealed partial class ExportJobsWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ExportJobsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessOneAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(processed ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogWorkerError(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IExportFileStorage>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ExportJobsHub>>();
        var reporting = scope.ServiceProvider.GetRequiredService<IReportingReadRepository>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await db.ExportJobs
            .FromSqlRaw("""
                SELECT * FROM analytics.export_jobs
                WHERE status = 'pending' AND deleted_at IS NULL
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (job is null)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        job.Status = "processing";
        job.StartedAt = DateTimeOffset.UtcNow;
        job.ProgressPct = 10;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await hub.Clients.Group(ExportJobsHub.GroupName(job.Id))
            .SendAsync("ExportProgress", new { jobId = job.Id, status = job.Status, progressPct = job.ProgressPct }, ct);

        try
        {
            var bytes = await BuildFileAsync(job, reporting, ct).ConfigureAwait(false);
            job.ProgressPct = 80;
            await db.SaveChangesAsync(ct);
            await hub.Clients.Group(ExportJobsHub.GroupName(job.Id))
                .SendAsync("ExportProgress", new { jobId = job.Id, status = job.Status, progressPct = 80 }, ct);

            await using var ms = new MemoryStream(bytes);
            var fileName = $"reporte-{job.ReportType}-{job.Id:N}.{Extension(job.Format)}";
            var stored = await storage.SaveExportAsync(job.Id, job.Format, fileName, ms, ct).ConfigureAwait(false);

            job.Status = "completed";
            job.ProgressPct = 100;
            job.FileStoragePath = stored.StoragePath;
            job.FileSha256 = stored.Sha256;
            job.FileSizeBytes = stored.SizeBytes;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await hub.Clients.Group(ExportJobsHub.GroupName(job.Id))
                .SendAsync("ExportCompleted", new { jobId = job.Id, status = "completed", progressPct = 100 }, ct);
            return true;
        }
        catch (Exception ex)
        {
            LogJobFailed(logger, ex, job.Id);
            job.Status = "failed";
            job.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await hub.Clients.Group(ExportJobsHub.GroupName(job.Id))
                .SendAsync("ExportCompleted", new { jobId = job.Id, status = "failed", progressPct = job.ProgressPct }, ct);
            return true;
        }
    }

    private static async Task<byte[]> BuildFileAsync(
        ExportJob job, IReportingReadRepository reporting, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(job.FiltersJson) ? "{}" : job.FiltersJson);
        var root = doc.RootElement;
        var from = root.TryGetProperty("from", out var f) && DateOnly.TryParse(f.GetString(), out var fd)
            ? fd : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var to = root.TryGetProperty("to", out var t) && DateOnly.TryParse(t.GetString(), out var td)
            ? td : DateOnly.FromDateTime(DateTime.UtcNow);

        if (string.Equals(job.ReportType, "procedures", StringComparison.OrdinalIgnoreCase))
        {
            var page = await reporting.GetProceduresAsync(
                new ReportingProceduresFilter(
                    job.TenantId, from, to, "created_at", null, null, null, null, "created_at", "desc"),
                1, 200, ct).ConfigureAwait(false);

            if (string.Equals(job.Format, "csv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(job.Format, "excel", StringComparison.OrdinalIgnoreCase)
                || string.Equals(job.Format, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder();
                sb.AppendLine("Id,Reference,Type,Status,Plate,VIN,OT,Company,CreatedAt,ElapsedHours");
                foreach (var row in page.Items)
                {
                    sb.Append(row.Id).Append(',')
                        .Append(Csv(row.ReferenceNumber)).Append(',')
                        .Append(Csv(row.ProcedureType)).Append(',')
                        .Append(Csv(row.Status)).Append(',')
                        .Append(Csv(row.Plate)).Append(',')
                        .Append(Csv(row.Vin)).Append(',')
                        .Append(Csv(row.TransitOfficeName)).Append(',')
                        .Append(Csv(row.CompanyName)).Append(',')
                        .Append(row.CreatedAt.ToString("O")).Append(',')
                        .Append(row.ElapsedHoursTotal?.ToString() ?? "")
                        .AppendLine();
                }
                return Encoding.UTF8.GetBytes(sb.ToString());
            }
        }

        var consolidado = await reporting.GetConsolidadoAsync(job.TenantId, from, to, "estado", ct).ConfigureAwait(false);
        return Encoding.UTF8.GetBytes(
            "Dimension,Key,Label,Total,Approved,Rejected,InProgress\n" +
            string.Join('\n', consolidado.Items.Select(r =>
                $"{r.Dimension},{Csv(r.Key)},{Csv(r.Label)},{r.Total},{r.Approved},{r.Rejected},{r.InProgress}")));
    }

    private static string Extension(string format) => format switch
    {
        "pdf" => "pdf",
        "excel" => "csv",
        _ => "csv",
    };

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "ExportJobsWorker error")]
    private static partial void LogWorkerError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Export job {JobId} failed")]
    private static partial void LogJobFailed(ILogger logger, Exception ex, Guid jobId);
}
