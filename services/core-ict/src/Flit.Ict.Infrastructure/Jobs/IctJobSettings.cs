using System.Data;
using Flit.Ict.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>
/// Snapshot inmutable de los parámetros de los jobs (cadencia/concurrencia/tamaño de lote). Lo sirve el
/// <see cref="IIctJobSettingsProvider"/> leyendo <c>ict.job_settings</c>; los jobs lo consultan cada ciclo
/// para su PollInterval, su concurrencia y su LIMIT de lote — así los cambios en BD aplican en caliente.
/// </summary>
public sealed record IctJobSettings
{
    public required int WindowStartHour { get; init; }

    public required int WindowEndHour { get; init; }

    public required int BusinessPollSeconds { get; init; }

    public required int ExternalPollSeconds { get; init; }

    public required int OrchestratorPollSeconds { get; init; }

    public required int OrchestratorConcurrency { get; init; }

    public required int OrchestratorBatchSize { get; init; }

    public required int SendPollSeconds { get; init; }

    public required int SendConcurrency { get; init; }

    public required int SendBatchSize { get; init; }

    public required int WebhookPollSeconds { get; init; }

    public required int WebhookBatchSize { get; init; }

    /// <summary>Defaults del código (fallback si <c>ict.job_settings</c> no tiene fila o falla la lectura).</summary>
    public static IctJobSettings FromOptions(IctJobOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new IctJobSettings
        {
            WindowStartHour = o.WindowStartHour,
            WindowEndHour = o.WindowEndHour,
            BusinessPollSeconds = o.BusinessPollSeconds,
            ExternalPollSeconds = o.ExternalPollSeconds,
            OrchestratorPollSeconds = o.OrchestratorPollSeconds,
            OrchestratorConcurrency = o.OrchestratorConcurrency,
            OrchestratorBatchSize = o.OrchestratorBatchSize,
            SendPollSeconds = o.SendPollSeconds,
            SendConcurrency = o.SendConcurrency,
            SendBatchSize = o.SendBatchSize,
            WebhookPollSeconds = o.WebhookPollSeconds,
            WebhookBatchSize = o.WebhookBatchSize,
        };
    }
}

/// <summary>Provee el snapshot vigente de parámetros de los jobs, refrescándolo desde BD con throttle.</summary>
public interface IIctJobSettingsProvider
{
    /// <summary>Último snapshot conocido (nunca null: arranca con los defaults del código).</summary>
    IctJobSettings Current { get; }

    /// <summary>Refresca el snapshot desde <c>ict.job_settings</c> si pasó el intervalo mínimo. Best-effort.</summary>
    Task RefreshAsync(CancellationToken ct = default);
}

/// <summary>
/// Lee <c>ict.job_settings</c> (fila única) y cachea el snapshot. Singleton: los 5 jobs comparten el
/// mismo estado y el throttle evita que la BD reciba más de una lectura cada <see cref="MinRefreshInterval"/>.
/// Ante fila ausente o error, conserva el último snapshot (o los defaults del código). No rompe los jobs.
/// </summary>
public sealed class IctJobSettingsProvider : IIctJobSettingsProvider
{
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IctJobSettings _defaults;
    private volatile IctJobSettings _current;
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private int _refreshing; // guard sin bloqueo: 0 = libre, 1 = refresco en curso

    public IctJobSettingsProvider(IServiceScopeFactory scopeFactory, IOptions<IctJobOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _scopeFactory = scopeFactory;
        _defaults = IctJobSettings.FromOptions(options.Value);
        _current = _defaults;
    }

    public IctJobSettings Current => _current;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (DateTime.UtcNow - _lastRefreshUtc < MinRefreshInterval)
        {
            return;
        }

        // Guard sin bloqueo: si ya hay un refresco en curso, este ciclo lo salta (no bloquea el job).
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (DateTime.UtcNow - _lastRefreshUtc < MinRefreshInterval)
            {
                return;
            }

            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (loaded is not null)
            {
                _current = loaded;
            }

            _lastRefreshUtc = DateTime.UtcNow;
        }
#pragma warning disable CA1031 // un fallo de lectura de config nunca debe romper el job; se conserva el snapshot
        catch (Exception)
        {
            // Mantener el último snapshot conocido.
        }
#pragma warning restore CA1031
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private async Task<IctJobSettings?> LoadAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT window_start_hour, window_end_hour, business_poll_seconds, external_poll_seconds,
                       orchestrator_poll_seconds, orchestrator_concurrency, orchestrator_batch_size,
                       send_poll_seconds, send_concurrency, send_batch_size,
                       webhook_poll_seconds, webhook_batch_size
                FROM ict.job_settings WHERE id = 1
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return null; // sin fila → se conservan los defaults/último snapshot
            }

            // Clamp defensivo: un poll/batch/concurrencia < 1 (mal editado) degeneraría en busy-loop o
            // en no procesar nada; se fuerza a >= 1. Las horas de ventana se leen tal cual (0-23 válidas).
            return new IctJobSettings
            {
                WindowStartHour = reader.GetInt16(0),
                WindowEndHour = reader.GetInt16(1),
                BusinessPollSeconds = AtLeast1(reader.GetInt32(2)),
                ExternalPollSeconds = AtLeast1(reader.GetInt32(3)),
                OrchestratorPollSeconds = AtLeast1(reader.GetInt32(4)),
                OrchestratorConcurrency = AtLeast1(reader.GetInt32(5)),
                OrchestratorBatchSize = AtLeast1(reader.GetInt32(6)),
                SendPollSeconds = AtLeast1(reader.GetInt32(7)),
                SendConcurrency = AtLeast1(reader.GetInt32(8)),
                SendBatchSize = AtLeast1(reader.GetInt32(9)),
                WebhookPollSeconds = AtLeast1(reader.GetInt32(10)),
                WebhookBatchSize = AtLeast1(reader.GetInt32(11)),
            };
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static int AtLeast1(int value) => value < 1 ? 1 : value;
}
