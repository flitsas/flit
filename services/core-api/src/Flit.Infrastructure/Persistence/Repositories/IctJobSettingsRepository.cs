using System.Data;
using System.Data.Common;
using Flit.Admin.Domain.Ict;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lee/escribe <c>ict.job_settings</c> (HU #12512). La tabla es de core-ict (DDL embebido,
/// sin RLS): no se mapea en el modelo EF de core-api para no generar migraciones de un schema
/// que no es dueño este servicio. SQL parametrizado, sin concatenación.
/// </summary>
internal sealed class IctJobSettingsRepository(FlitDbContext db) : IIctJobSettingsRepository
{
    public async Task<IctJobSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await ExecuteReadAsync(cancellationToken).ConfigureAwait(false);
        return loaded ?? IctJobSettings.Defaults();
    }

    public async Task<IctJobSettings> SaveAsync(IctJobSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var conn = db.Database.GetDbConnection();
            var wasClosed = conn.State != ConnectionState.Open;
            if (wasClosed)
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO ict.job_settings (
                        id, window_start_hour, window_end_hour,
                        business_poll_seconds, external_poll_seconds,
                        orchestrator_poll_seconds, orchestrator_concurrency, orchestrator_batch_size,
                        send_poll_seconds, send_concurrency, send_batch_size,
                        webhook_poll_seconds, webhook_batch_size,
                        business_batch_size, external_batch_size,
                        updated_at, updated_by)
                    VALUES (
                        1, @windowStart, @windowEnd,
                        @businessPoll, @externalPoll,
                        @orchPoll, @orchConc, @orchBatch,
                        @sendPoll, @sendConc, @sendBatch,
                        @webhookPoll, @webhookBatch,
                        @businessBatch, @externalBatch,
                        @updatedAt, @updatedBy)
                    ON CONFLICT (id) DO UPDATE SET
                        window_start_hour = EXCLUDED.window_start_hour,
                        window_end_hour = EXCLUDED.window_end_hour,
                        business_poll_seconds = EXCLUDED.business_poll_seconds,
                        external_poll_seconds = EXCLUDED.external_poll_seconds,
                        orchestrator_poll_seconds = EXCLUDED.orchestrator_poll_seconds,
                        orchestrator_concurrency = EXCLUDED.orchestrator_concurrency,
                        orchestrator_batch_size = EXCLUDED.orchestrator_batch_size,
                        send_poll_seconds = EXCLUDED.send_poll_seconds,
                        send_concurrency = EXCLUDED.send_concurrency,
                        send_batch_size = EXCLUDED.send_batch_size,
                        webhook_poll_seconds = EXCLUDED.webhook_poll_seconds,
                        webhook_batch_size = EXCLUDED.webhook_batch_size,
                        business_batch_size = EXCLUDED.business_batch_size,
                        external_batch_size = EXCLUDED.external_batch_size,
                        updated_at = EXCLUDED.updated_at,
                        updated_by = EXCLUDED.updated_by
                    """;
                AddParam(cmd, "windowStart", (short)settings.WindowStartHour);
                AddParam(cmd, "windowEnd", (short)settings.WindowEndHour);
                AddParam(cmd, "businessPoll", settings.BusinessPollSeconds);
                AddParam(cmd, "externalPoll", settings.ExternalPollSeconds);
                AddParam(cmd, "orchPoll", settings.OrchestratorPollSeconds);
                AddParam(cmd, "orchConc", settings.OrchestratorConcurrency);
                AddParam(cmd, "orchBatch", settings.OrchestratorBatchSize);
                AddParam(cmd, "sendPoll", settings.SendPollSeconds);
                AddParam(cmd, "sendConc", settings.SendConcurrency);
                AddParam(cmd, "sendBatch", settings.SendBatchSize);
                AddParam(cmd, "webhookPoll", settings.WebhookPollSeconds);
                AddParam(cmd, "webhookBatch", settings.WebhookBatchSize);
                AddParam(cmd, "businessBatch", settings.BusinessBatchSize);
                AddParam(cmd, "externalBatch", settings.ExternalBatchSize);
                AddParam(cmd, "updatedAt", settings.UpdatedAt);
                AddParam(cmd, "updatedBy", (object?)settings.UpdatedBy ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (wasClosed)
                {
                    await conn.CloseAsync().ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IctJobSettings?> ExecuteReadAsync(CancellationToken ct)
    {
        IctJobSettings? loaded = null;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var conn = db.Database.GetDbConnection();
            var wasClosed = conn.State != ConnectionState.Open;
            if (wasClosed)
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
            }

            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT window_start_hour, window_end_hour, business_poll_seconds, external_poll_seconds,
                           orchestrator_poll_seconds, orchestrator_concurrency, orchestrator_batch_size,
                           send_poll_seconds, send_concurrency, send_batch_size,
                           webhook_poll_seconds, webhook_batch_size,
                           business_batch_size, external_batch_size,
                           updated_at, updated_by
                    FROM ict.job_settings WHERE id = 1
                    """;
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    loaded = null;
                    return;
                }

                loaded = new IctJobSettings(
                    reader.GetInt16(0),
                    reader.GetInt16(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11),
                    reader.GetInt32(12),
                    reader.GetInt32(13),
                    ReadTimestamp(reader, 14),
                    reader.IsDBNull(15) ? null : reader.GetGuid(15));
            }
            finally
            {
                if (wasClosed)
                {
                    await conn.CloseAsync().ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);

        return loaded;
    }

    private static DateTimeOffset ReadTimestamp(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return default;
        }

        return reader.GetFieldType(ordinal) == typeof(DateTimeOffset)
            ? reader.GetFieldValue<DateTimeOffset>(ordinal)
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
