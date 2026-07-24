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
public sealed class WebhookNotificationJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    ILogger<WebhookNotificationJob> logger) : IctPollingJob(scopeFactory, options, logger)
{
    private const int MaxAttempts = 8;

    private sealed record PendingWebhook(
        Guid Id, string TargetUrl, string ManagerIdTransaction, string IctEstado,
        string Message, int TransactionType, short Attempts, short StatusValidation);

    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(Options.WebhookPollSeconds);

    protected override string JobName => "webhook-notification";

    protected override async Task RunCycleAsync(IServiceScope scope, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ict-webhook");
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            var pending = await ReadPendingAsync(connection, ct);
            foreach (var wh in pending)
            {
                if (string.IsNullOrWhiteSpace(wh.TargetUrl))
                {
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
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<bool> TryDeliverAsync(HttpClient http, PendingWebhook wh, CancellationToken ct)
    {
        try
        {
            // Forma de payload v1 (clientes existentes): no cambiar los nombres de campo. v1 emite
            // `status` como NÚMERO (no cadena) y adjunta statusText/message; se calca exactamente.
            var description = DescribeStatus(wh.StatusValidation);
            var payload = new
            {
                transactionFlit = wh.ManagerIdTransaction,
                managerIdTransaction = wh.ManagerIdTransaction,
                status = (int)wh.StatusValidation,
                statusDescription = description,
                statusMessage = wh.Message,
                statusObservation = string.Empty,
                statusText = description,
                message = wh.Message,
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

    private static async Task<List<PendingWebhook>> ReadPendingAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, target_url, manager_id_transaction, ict_estado, message_validation, transaction_type,
                   attempts, status_validation
            FROM ict.external_integration_webhook_master
            WHERE is_notified = false AND next_attempt_at <= now()
            ORDER BY created_at
            LIMIT 50
            """;
        var list = new List<PendingWebhook>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PendingWebhook(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetInt16(6), reader.GetInt16(7)));
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
}
