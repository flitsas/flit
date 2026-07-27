using System.Data;
using System.Data.Common;
using System.Text.Json;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Validation;
using Flit.Ict.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>
/// Job 3 (v1 ExternalOrchestratorProcessPending): consulta las fuentes externas de cada
/// source_query pendiente, aplica los validadores (SOAT/RTM/RNMC/paz y salvo) y marca la respuesta.
/// Si algún validador bloquea, el pre-trámite queda con novedades. Corre como job de sistema
/// (superusuario en dev; en producción requiere un rol con BYPASSRLS — TODO(ICT-RLS-SP)).
/// </summary>
public sealed class OrchestratorJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    ILogger<OrchestratorJob> logger) : IctPollingJob(scopeFactory, options, logger)
{
    /// <summary>Tope de reintentos por fuente ante fallo TÉCNICO (core-api caído/timeout); al agotar, novedad.</summary>
    private const int MaxAttempts = 3;

    private sealed record PendingQuery(
        Guid Id, Guid MasterId, Guid TenantId, string QueryType,
        string Plate, string Vin, string DocumentType, string DocumentNumber, int TransactionType, short Attempts);

    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(Options.OrchestratorPollSeconds);

    protected override string JobName => "orchestrator";

    protected override async Task RunCycleAsync(IServiceScope scope, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();
        var consult = scope.ServiceProvider.GetRequiredService<IConsultationClient>();
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            var pending = await ReadPendingAsync(connection, ct);
            var currentYear = DateTime.UtcNow.Year;

            foreach (var q in pending)
            {
                try
                {
                    var result = await consult.QueryAsync(
                        q.TenantId, q.QueryType, q.Plate, q.Vin, q.DocumentType, q.DocumentNumber, ct);
                    var issues = ExternalSourceValidators.Validate(q.TransactionType, q.QueryType, result, currentYear);
                    var warnings = ExternalSourceValidators.Warnings(result);
                    var isValid = issues.Count == 0;

                    await InsertResponseAsync(connection, q.Id, q.TenantId, JsonSerializer.Serialize(result), ct);
                    await MarkQueriedAsync(connection, q.Id, isValid, ct);

                    // Novedad de NEGOCIO BLOQUEANTE (SOAT/RTM/RNMC): terminal, deja el master en ps=4.
                    if (!isValid)
                    {
                        await FlagNoveltyAsync(connection, q.MasterId, string.Join("; ", issues), ct);
                    }
                    // Advertencia INFORMATIVA (paz y salvo del conductor/DRIVER): fiel a v1, se registra en el
                    // master pero NO bloquea el paso a borrador (el OT decidirá). Visible en el estado.
                    else if (warnings.Count > 0)
                    {
                        await RecordWarningAsync(connection, q.MasterId, string.Join("; ", warnings), ct);
                    }
                }
#pragma warning disable CA1031 // el orquestador debe ser resiliente a CUALQUIER fallo técnico de la fuente
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    // Fallo TÉCNICO de la fuente (core-api caído, ErrorCode gRPC, timeout): reintentar hasta
                    // MaxAttempts. Una fila que falla NO aborta el resto del lote. Al AGOTAR los intentos se
                    // deja el master con novedades (ps=4) con el MENSAJE del fallo: el cliente lo ve en el
                    // estado (messageValidation/statusObservation/statusProcess[].observation) y corrige. El
                    // response v1 NO cambia: se reusa el mismo camino (external_comments_validation + record_process_status).
                    await RecordAttemptFailureAsync(connection, q.Id, ct);
                    if (q.Attempts + 1 >= MaxAttempts)
                    {
                        // FlagNovelty PRIMERO (deja el master en ps=4, excluido del gate de materialización
                        // aunque lo siguiente fallara); luego cerrar la source_query para liberar el gate.
                        await FlagNoveltyAsync(connection, q.MasterId,
                            $"Fuentes externas no disponibles tras {MaxAttempts} intentos: {ex.Message}", ct);
                        await MarkQueriedAsync(connection, q.Id, isValid: false, ct);
                    }

                    IctJobLog.CycleError(logger, ex, JobName);
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

    private static async Task<List<PendingQuery>> ReadPendingAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT sq.id, sq.eim_id, sq.tenant_id, sq.query_type, sq.plate_complete, sq.vehicle_vin,
                   sq.document_type, sq.document_number, m.transaction_type, sq.attempts
            FROM ict.external_integration_source_query sq
            JOIN ict.external_integration_master m ON m.id = sq.eim_id
            WHERE sq.is_data_queried = false AND sq.attempts < @max
            ORDER BY sq.created_at
            LIMIT 50
            """;
        AddParam(cmd, "max", (short)MaxAttempts);
        var list = new List<PendingQuery>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PendingQuery(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetInt32(8), reader.GetInt16(9)));
        }

        return list;
    }

    private static async Task InsertResponseAsync(DbConnection connection, Guid queryId, Guid tenantId, string json, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ict.external_integration_source_response (eisq_id, tenant_id, query_response)
            VALUES (@id, @tenant, @json::jsonb)
            """;
        AddParam(cmd, "id", queryId);
        AddParam(cmd, "tenant", tenantId);
        AddParam(cmd, "json", json);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkQueriedAsync(DbConnection connection, Guid queryId, bool isValid, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE ict.external_integration_source_query
            SET is_data_queried = true, is_data_valid = @valid, updated_at = now()
            WHERE id = @id
            """;
        AddParam(cmd, "id", queryId);
        AddParam(cmd, "valid", isValid);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Registra una advertencia INFORMATIVA en el master (external_comments_validation) SIN bloquear: no
    /// cambia process_status_id, así que el master avanza a borrador con la observación visible en el estado.
    /// </summary>
    private static async Task RecordWarningAsync(DbConnection connection, Guid masterId, string message, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE ict.external_integration_master
            SET external_comments_validation = external_comments_validation || @msg
            WHERE id = @id;
            SELECT ict.record_pretramite_event(m.id, m.tenant_id, 'en_validacion_externa', 'advertencia',
                       jsonb_build_object('warnings', @msg))
            FROM ict.external_integration_master m WHERE m.id = @id
            """;
        AddParam(cmd, "id", masterId);
        AddParam(cmd, "msg", " " + message + ";");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Incrementa el contador de intentos de una source_query tras un fallo técnico.</summary>
    private static async Task RecordAttemptFailureAsync(DbConnection connection, Guid queryId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE ict.external_integration_source_query
            SET attempts = attempts + 1, updated_at = now()
            WHERE id = @id
            """;
        AddParam(cmd, "id", queryId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task FlagNoveltyAsync(DbConnection connection, Guid masterId, string message, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE ict.external_integration_master
            SET process_status_id = 4,
                external_comments_validation = external_comments_validation || @msg
            WHERE id = @id;
            SELECT ict.record_process_status(m.id, m.tenant_id, 4,
                       'CON NOVEDADES EN FUENTES EXTERNAS:' || @msg,
                       m.manager_user, m.manager_mail, m.company_manager_document)
            FROM ict.external_integration_master m WHERE m.id = @id;
            INSERT INTO ict.external_integration_webhook_master
                (id_transaction, tenant_id, manager_id_transaction, transaction_type, status_validation,
                 message_validation, ict_estado, target_url)
            SELECT m.id, m.tenant_id, m.manager_id_transaction, m.transaction_type, 4,
                   'CON NOVEDADES EN FUENTES EXTERNAS:' || @msg, 'con_novedades', m.url_web_hook
            FROM ict.external_integration_master m WHERE m.id = @id;
            SELECT ict.record_pretramite_event(m.id, m.tenant_id, 'con_novedades', 'con_novedades',
                       jsonb_build_object('issues', @msg))
            FROM ict.external_integration_master m WHERE m.id = @id
            """;
        AddParam(cmd, "id", masterId);
        AddParam(cmd, "msg", " " + message + ";");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
