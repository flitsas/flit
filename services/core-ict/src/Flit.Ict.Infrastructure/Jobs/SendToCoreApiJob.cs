using System.Data;
using System.Data.Common;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>
/// Job 4 (v1 ExternalSourceResponseProcessAndSend): materializa el borrador en core-api (gRPC) para
/// los pre-trámites validados. Mapea transaction_type -> ProcedureType code; si el tipo no está
/// publicado en v2, queda con novedades. Si el gRPC no está disponible, deja el pre-trámite para el
/// siguiente ciclo (no marca novedad).
///
/// GATING: solo materializa cuando el orquestador (Job 3) YA consultó todas las fuentes del master (no
/// quedan source_query con is_data_queried=false). Así el borrador nunca se crea antes de validar las
/// fuentes externas. Una novedad del orquestador deja process_status_id=4 (excluido por el filtro), de
/// modo que aquí solo entran masters con negocio validado Y todas las fuentes consultadas y válidas.
///
/// ESPERA DE ADJUNTOS (paridad v1): además exige (closed_document = true OR
/// process_without_attached_documents = true) — la misma cláusula del gate v1 getListSourceQuery
/// (BackApiExternalTransact/.../sourceQueryRepository.ts). Con AMBAS banderas en false el pre-trámite NO
/// materializa: se queda esperando indefinidamente a que el cliente CIERRE el documento (closed_document=
/// true) tras subir los adjuntos, o a que declare el waiver (process_without_attached_documents=true). El
/// cierre lo fija <c>CloseDocumentHandler</c> / el upload v1 con closed=true.
/// </summary>
public sealed class SendToCoreApiJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    IIctJobSettingsProvider settings,
    ILogger<SendToCoreApiJob> logger) : IctPollingJob(scopeFactory, options, settings, logger)
{
    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(JobSettings.SendPollSeconds);

    protected override string JobName => "send-to-core-api";

    protected override Task RunCycleAsync(IServiceScope scope, CancellationToken ct) =>
        // Advisory lock: guarda multi-réplica (solo una réplica materializa el lote por ciclo).
        RunUnderAdvisoryLockAsync(
            scope, IctAdvisoryLock.Keys.SendToCoreApi,
            _ => MaterializeAsync(scope, JobSettings.SendBatchSize, JobSettings.SendConcurrency, ct), ct);

    private async Task MaterializeAsync(IServiceScope scope, int batchSize, int concurrency, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();

        // Catálogo de mapeo (global, pequeño) proyectado a datos planos: se pasa a las tareas paralelas sin
        // arrastrar entidades rastreadas por un DbContext entre scopes.
        var mappingRows = await db.ProcedureTypeMappings
            .AsNoTracking()
            .Select(m => new { m.ExternalTransactionType, m.IsPublished, m.ProcedureTypeCode })
            .ToListAsync(ct);
        var mappings = mappingRows.ToDictionary(m => m.ExternalTransactionType, m => (m.IsPublished, m.ProcedureTypeCode));

        // Solo masters cuyas fuentes externas ya fueron TODAS consultadas por el orquestador.
        var readyIds = await ReadReadyMasterIdsAsync(db, batchSize, ct);
        if (readyIds.Count == 0)
        {
            return;
        }

        // Materialización en PARALELO ACOTADO: el gRPC (lo lento) de cada master corre concurrente, cada
        // uno en su propio scope/DbContext/conexión (thread-safe), gateado por el semáforo. Antes era un
        // foreach secuencial — el cuello de botella con miles de pre-trámites. El advisory lock de la
        // réplica sigue garantizando que solo UNA instancia procese el lote (sin doble materialización).
        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        await Task.WhenAll(readyIds.Select(id => ProcessMasterAsync(gate, id, mappings, ct)));
    }

    /// <summary>Materializa UN master en su propio scope/DbContext (thread-safe), gateado por el semáforo.</summary>
    private async Task ProcessMasterAsync(
        SemaphoreSlim gate,
        Guid masterId,
        Dictionary<short, (bool IsPublished, string ProcedureTypeCode)> mappings,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var scope = ScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();
            var draftClient = scope.ServiceProvider.GetRequiredService<IProcedureDraftClient>();

            var master = await db.Masters
                .Include(m => m.Actors)
                .Include(m => m.Attachments)
                .FirstOrDefaultAsync(m => m.Id == masterId, ct);
            if (master is null)
            {
                return;
            }

            if (!mappings.TryGetValue((short)master.TransactionType, out var mapping) || !mapping.IsPublished)
            {
                await FlagNoveltyAsync(db, master, "tipo de trámite no soportado en v2 (modalidad_not_available)", ct);
                return;
            }

            var result = await draftClient.CreateDraftAsync(master, mapping.ProcedureTypeCode, ct);

            if (result.ErrorCode == "grpc_unavailable")
            {
                // gRPC pendiente. No cambiar estado; se reintenta en el siguiente ciclo.
                return;
            }

            if (result.ProcedureInstanceId is { } instanceId)
            {
                master.ProcedureInstanceId = instanceId;
                master.ProcessStatusId = 5; // BORRADOR (terminal en ICT tras materializar)
                await db.SaveChangesAsync(ct);

                // starts_procedure_in_paused (contrato v1): el borrador nace pausado, con la
                // observación que el gestor quiere ver en el dashboard.
                if (master.StartsProcedureInPaused)
                {
                    await draftClient.PauseDraftAsync(
                        master.TenantId, instanceId, paused: true,
                        master.ObservationWhenPaused ?? string.Empty,
                        master.ManagerUser, master.ManagerMail, master.CompanyManagerDocument, ct);
                }

                // Histórico v1: el trámite pasa por Procesado (3) y luego Borrador (5).
                await RecordStatusAsync(db, master, 3, "PROCESADO SATISFACTORIAMENTE", ct);
                await RecordEventAsync(db, master, "procesado", "ok", null, ct);
                await RecordStatusAsync(db, master, 5, "BORRADOR CREADO EN LA PLATAFORMA", ct);
                await EnqueueWebhookAsync(db, master, 5, "borrador_creado", "BORRADOR CREADO", ct);
                await RecordEventAsync(db, master, "borrador_creado", "ok", null, ct);
            }
            else
            {
                await FlagNoveltyAsync(db, master, result.ErrorCode ?? "error al crear el borrador", ct);
            }
        }
#pragma warning disable CA1031 // un master fallido no debe abortar los demás del lote; se reintenta el siguiente ciclo
        catch (Exception ex)
        {
            IctJobLog.CycleError(logger, ex, JobName);
        }
#pragma warning restore CA1031
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Ids de masters listos para materializar: negocio+externo validados, sin materializar, con el
    /// orquestador terminado (ninguna source_query pendiente) Y con el documento cerrado o el waiver de
    /// adjuntos. El NOT EXISTS es el gate que impide crear el borrador antes de consultar las fuentes; la
    /// cláusula (closed_document OR process_without_attached_documents) es el gate que impide crearlo antes
    /// de que el cliente termine de subir los adjuntos (paridad v1 getListSourceQuery). Un master sin
    /// fuentes (ninguna source_query) también pasa el NOT EXISTS. RLS la saltan los jobs (superusuario dev
    /// / BYPASSRLS prod).
    /// </summary>
    private static async Task<List<Guid>> ReadReadyMasterIdsAsync(IctDbContext db, int limit, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT m.id
                FROM ict.external_integration_master m
                WHERE m.external_validation = 2 AND m.business_validation = 2
                  AND m.process_status_id = 2 AND m.procedure_instance_id IS NULL AND m.deleted_at IS NULL
                  AND (m.closed_document = true OR m.process_without_attached_documents = true)
                  AND NOT EXISTS (
                      SELECT 1 FROM ict.external_integration_source_query sq
                      WHERE sq.eim_id = m.id AND sq.is_data_queried = false)
                ORDER BY m.created_at
                LIMIT @limit
                """;
            AddParam(cmd, "limit", limit);
            var ids = new List<Guid>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ids.Add(reader.GetGuid(0));
            }

            return ids;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static async Task FlagNoveltyAsync(IctDbContext db, ExternalIntegrationMaster master, string message, CancellationToken ct)
    {
        master.ProcessStatusId = 4;
        master.ExternalCommentsValidation = (master.ExternalCommentsValidation + " " + message + ";").Trim();
        await db.SaveChangesAsync(ct);
        await RecordStatusAsync(db, master, 4, "CON NOVEDADES: " + message, ct);
        await EnqueueWebhookAsync(db, master, 4, "con_novedades", "CON NOVEDADES: " + message, ct);
        await RecordEventAsync(db, master, "con_novedades", "con_novedades", message, ct);
    }

    /// <summary>Registra una transición de estado en el histórico v1 (colapsa sub-pasos por estado).</summary>
    private static Task<int> RecordStatusAsync(
        IctDbContext db,
        ExternalIntegrationMaster master,
        int code,
        string message,
        CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT ict.record_process_status({master.Id}, {master.TenantId}, {code}, {message},
                {master.ManagerUser}, {master.ManagerMail}, {master.CompanyManagerDocument})
            """, ct);

    private static Task<int> EnqueueWebhookAsync(
        IctDbContext db,
        ExternalIntegrationMaster master,
        short statusValidation,
        string ictEstado,
        string message,
        CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ict.external_integration_webhook_master
                (id_transaction, tenant_id, manager_id_transaction, transaction_type, status_validation,
                 message_validation, ict_estado, target_url)
            VALUES ({master.Id}, {master.TenantId}, {master.ManagerIdTransaction}, {master.TransactionType},
                 {statusValidation}, {message}, {ictEstado}, {master.UrlWebHook})
            """, ct);

    /// <summary>Emite un evento al timeline de negocio (ict.pretramite_events). detail por allowlist.</summary>
    private static Task<int> RecordEventAsync(
        IctDbContext db,
        ExternalIntegrationMaster master,
        string stage,
        string outcome,
        string? message,
        CancellationToken ct) =>
        // Cast explícito de los params nullable: un NULL sin tipo dentro de jsonb_build_object hace
        // que Postgres no pueda inferir el tipo del parámetro (42P18).
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT ict.record_pretramite_event({master.Id}, {master.TenantId}, {stage}, {outcome},
                jsonb_build_object('transaction_type', {master.TransactionType},
                                   'procedure_instance_id', {master.ProcedureInstanceId}::uuid,
                                   'message', {message}::text))
            """, ct);
}
