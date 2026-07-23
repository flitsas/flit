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

        var ready = await db.Masters
            .Include(m => m.Actors)
            .Where(m => m.ExternalValidation == 2 && m.BusinessValidation == 2
                        && m.ProcessStatusId == 2 && m.ProcedureInstanceId == null && m.DeletedAt == null)
            .Take(50)
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
                master.ProcessStatusId = 3; // PROCESADO
                await db.SaveChangesAsync(ct);
                await EnqueueWebhookAsync(db, master, 3, "borrador_creado", "PROCESADO", ct);
            }
            else
            {
                await FlagNoveltyAsync(db, master, result.ErrorCode ?? "error al crear el borrador", ct);
            }
        }
    }

    private static async Task FlagNoveltyAsync(IctDbContext db, ExternalIntegrationMaster master, string message, CancellationToken ct)
    {
        master.ProcessStatusId = 4;
        master.ExternalCommentsValidation = (master.ExternalCommentsValidation + " " + message + ";").Trim();
        await db.SaveChangesAsync(ct);
        await EnqueueWebhookAsync(db, master, 4, "con_novedades", "CON NOVEDADES: " + message, ct);
    }

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
