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
/// </summary>
public sealed class SendToCoreApiJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    ILogger<SendToCoreApiJob> logger) : IctPollingJob(scopeFactory, options, logger)
{
    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(Options.SendPollSeconds);

    protected override string JobName => "send-to-core-api";

    protected override async Task RunCycleAsync(IServiceScope scope, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<IctDbContext>();
        var draftClient = scope.ServiceProvider.GetRequiredService<IProcedureDraftClient>();

        var mappings = await db.ProcedureTypeMappings
            .ToDictionaryAsync(m => m.ExternalTransactionType, m => m, ct);

        // Solo masters cuyas fuentes externas ya fueron TODAS consultadas por el orquestador.
        var readyIds = await ReadReadyMasterIdsAsync(db, ct);
        if (readyIds.Count == 0)
        {
            return;
        }

        var ready = await db.Masters
            .Include(m => m.Actors)
            .Include(m => m.Attachments)
            .Where(m => readyIds.Contains(m.Id))
            .ToListAsync(ct);

        foreach (var master in ready)
        {
            if (!mappings.TryGetValue((short)master.TransactionType, out var mapping) || !mapping.IsPublished)
            {
                await FlagNoveltyAsync(db, master, "tipo de trámite no soportado en v2 (modalidad_not_available)", ct);
                continue;
            }

            var result = await draftClient.CreateDraftAsync(master, mapping.ProcedureTypeCode, ct);

            if (result.ErrorCode == "grpc_unavailable")
            {
                // gRPC pendiente (HU4). No cambiar estado; se reintenta en el siguiente ciclo.
                continue;
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
                await RecordStatusAsync(db, master, 5, "BORRADOR CREADO EN LA PLATAFORMA", ct);
                await EnqueueWebhookAsync(db, master, 5, "borrador_creado", "BORRADOR CREADO", ct);
            }
            else
            {
                await FlagNoveltyAsync(db, master, result.ErrorCode ?? "error al crear el borrador", ct);
            }
        }
    }

    /// <summary>
    /// Ids de masters listos para materializar: negocio+externo validados, sin materializar, y con el
    /// orquestador terminado (ninguna source_query pendiente). El NOT EXISTS es el gate que impide crear
    /// el borrador antes de consultar las fuentes. Un master sin fuentes (ninguna source_query) también
    /// pasa (no hay nada que consultar). RLS la saltan los jobs (superusuario dev / BYPASSRLS prod).
    /// </summary>
    private static async Task<List<Guid>> ReadReadyMasterIdsAsync(IctDbContext db, CancellationToken ct)
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
                  AND NOT EXISTS (
                      SELECT 1 FROM ict.external_integration_source_query sq
                      WHERE sq.eim_id = m.id AND sq.is_data_queried = false)
                ORDER BY m.created_at
                LIMIT 50
                """;
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

    private static async Task FlagNoveltyAsync(IctDbContext db, ExternalIntegrationMaster master, string message, CancellationToken ct)
    {
        master.ProcessStatusId = 4;
        master.ExternalCommentsValidation = (master.ExternalCommentsValidation + " " + message + ";").Trim();
        await db.SaveChangesAsync(ct);
        await RecordStatusAsync(db, master, 4, "CON NOVEDADES: " + message, ct);
        await EnqueueWebhookAsync(db, master, 4, "con_novedades", "CON NOVEDADES: " + message, ct);
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
}
