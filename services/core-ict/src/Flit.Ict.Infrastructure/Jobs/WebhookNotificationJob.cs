using System.Data;
using System.Data.Common;
using System.Net.Http.Json;
using Flit.Ict.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>
/// Job 5 (v1 ExternalWebhookNotifications): entrega los webhooks pendientes al gestor con el
/// vocabulario v2. Reintentos con backoff + dead-letter. Mejora sobre v1: PollInterval de segundos
/// (objetivo &lt;9 min holgado). El payload nunca usa los códigos numéricos v1.
/// </summary>
public sealed partial class WebhookNotificationJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    IIctJobSettingsProvider settings,
    ILogger<WebhookNotificationJob> logger) : IctPollingJob(scopeFactory, options, settings, logger)
{
    private const int MaxAttempts = 8;

    private sealed record PendingWebhook(
        Guid Id, string TargetUrl, string ManagerIdTransaction, string IctEstado,
        string Message, int TransactionType, short Attempts, short StatusValidation, Guid? ProcedureInstanceId);

    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(JobSettings.WebhookPollSeconds);

    protected override string JobName => "webhook-notification";

    protected override Task RunCycleAsync(IServiceScope scope, CancellationToken ct) =>
        // Advisory lock (guarda multi-réplica): solo UNA réplica entrega el lote por ciclo, lo que evita la
        // DOBLE ENTREGA del mismo webhook cuando hay 2+ réplicas (antes este job era el único sin el lock).
        // Se prefiere sobre FOR UPDATE SKIP LOCKED para no retener locks de fila durante la entrega HTTP
        // (lenta); es el mismo mecanismo que ya usan los otros 4 jobs del pipeline. La conexión (abierta y
        // con el lock) la provee RunUnderAdvisoryLockAsync.
        RunUnderAdvisoryLockAsync(
            scope, IctAdvisoryLock.Keys.Webhook, connection => DeliverPendingAsync(scope, connection, ct), ct);

    private async Task DeliverPendingAsync(IServiceScope scope, DbConnection connection, CancellationToken ct)
    {
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ict-webhook");
        var pending = await ReadPendingAsync(connection, JobSettings.WebhookBatchSize, ct);
        foreach (var wh in pending)
        {
            // Anti-SSRF: el target_url viene del payload de ingesta. Un destino interno/privado o un
            // esquema no-http se descarta como fallo terminal (no reintentar; no se arregla solo).
            if (!await WebhookTargetGuard.IsPublicHttpTargetAsync(wh.TargetUrl, ct))
            {
                Log.TargetBlocked(logger, wh.Id, wh.TargetUrl);
                await MarkDeliveredAsync(connection, wh.Id, responseOk: false, ct);
                continue;
            }

            var delivered = await TryDeliverAsync(http, wh, ct);
            if (delivered)
            {
                await MarkDeliveredAsync(connection, wh.Id, responseOk: true, ct);
            }
            else
            {
                await ScheduleRetryAsync(connection, wh.Id, wh.Attempts, ct);
            }
        }
    }

    private async Task<bool> TryDeliverAsync(HttpClient http, PendingWebhook wh, CancellationToken ct)
    {
        try
        {
            // Payload con vocabulario v2 (plan §A.9): el estado v2 del pre-trámite (ictEstado) y la
            // correlación del trámite (procedureInstanceId) son los campos primarios. Se CONSERVAN los
            // campos v1 (status numérico, statusText, …) por compatibilidad con los gestores existentes
            // migrados desde v1; no se rompe su contrato.
            // TODO(ICT-WEBHOOK-V1-COMPAT): retirar los campos numéricos v1 cuando los gestores confirmen v2.
            var description = DescribeStatus(wh.StatusValidation);
            var payload = new
            {
                managerIdTransaction = wh.ManagerIdTransaction,
                ictEstado = wh.IctEstado,
                procedureInstanceId = wh.ProcedureInstanceId,
                transactionType = wh.TransactionType,
                message = wh.Message,
                timestamp = DateTimeOffset.UtcNow,
                // ── Compat v1 ──
                transactionFlit = wh.ManagerIdTransaction,
                status = (int)wh.StatusValidation,
                statusDescription = description,
                statusMessage = wh.Message,
                statusObservation = string.Empty,
                statusText = description,
            };
            using var response = await http.PostAsJsonAsync(new Uri(wh.TargetUrl), payload, ct);
            return response.IsSuccessStatusCode;
        }
#pragma warning disable CA1031 // fallo de entrega -> reintento con backoff
        catch (Exception ex)
        {
            IctJobLog.CycleError(logger, ex, "webhook-delivery");
            return false;
        }
#pragma warning restore CA1031
    }

    private static async Task<List<PendingWebhook>> ReadPendingAsync(DbConnection connection, int limit, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        // JOIN al master para adjuntar la correlación del trámite (procedure_instance_id) al payload v2.
        cmd.CommandText = """
            SELECT w.id, w.target_url, w.manager_id_transaction, w.ict_estado, w.message_validation,
                   w.transaction_type, w.attempts, w.status_validation, m.procedure_instance_id
            FROM ict.external_integration_webhook_master w
            LEFT JOIN ict.external_integration_master m ON m.id = w.id_transaction
            WHERE w.is_notified = false AND w.next_attempt_at <= now()
            ORDER BY w.created_at
            LIMIT @limit
            """;
        AddParam(cmd, "limit", limit);
        var list = new List<PendingWebhook>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PendingWebhook(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetInt16(6), reader.GetInt16(7),
                await reader.IsDBNullAsync(8, ct) ? null : reader.GetGuid(8)));
        }

        return list;
    }

    private static async Task MarkDeliveredAsync(DbConnection connection, Guid id, bool responseOk, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE ict.external_integration_webhook_master
            SET is_notified = true, response_ok = @ok, date_notified = now(), attempts = attempts + 1, updated_at = now()
            WHERE id = @id
            """;
        AddParam(cmd, "id", id);
        AddParam(cmd, "ok", responseOk);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ScheduleRetryAsync(DbConnection connection, Guid id, short attempts, CancellationToken ct)
    {
        var deadLetter = attempts + 1 >= MaxAttempts;
        var backoffSeconds = Math.Min((attempts + 1) * 30, 900);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = deadLetter
            ? """
              UPDATE ict.external_integration_webhook_master
              SET is_notified = true, response_ok = false, attempts = attempts + 1, updated_at = now()
              WHERE id = @id
              """
            : """
              UPDATE ict.external_integration_webhook_master
              SET attempts = attempts + 1, next_attempt_at = now() + make_interval(secs => @secs), updated_at = now()
              WHERE id = @id
              """;
        AddParam(cmd, "id", id);
        if (!deadLetter)
        {
            AddParam(cmd, "secs", (double)backoffSeconds);
        }

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    /// <summary>Descripción v1 por código de estado (para el payload del webhook).</summary>
    private static string DescribeStatus(short code) => code switch
    {
        1 => "Registrado",
        2 => "En Validacion",
        3 => "Procesado",
        4 => "Con Novedades",
        5 => "Borrador",
        6 => "Anulado",
        _ => string.Empty,
    };

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Webhook {WebhookId} bloqueado por destino no público (anti-SSRF): {TargetUrl}. No se reintenta.")]
        public static partial void TargetBlocked(ILogger logger, Guid webhookId, string targetUrl);
    }
}
