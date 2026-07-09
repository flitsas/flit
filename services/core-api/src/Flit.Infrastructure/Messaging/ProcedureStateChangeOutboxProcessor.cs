using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Procesador de la outbox de cambios de estado del trámite (N 03 RNF01, ADR-0022). Sondea
/// <c>tramites.procedure_state_change_outbox</c> en busca de filas pendientes
/// (<c>published_at IS NULL</c>), las despacha vía <see cref="IProcedureStateChangeNotifier"/>
/// (webhooks OT) y sella <c>published_at</c>. Ante fallo incrementa <c>attempts</c>; al alcanzar
/// <see cref="MaxAttempts"/> la fila queda en dead-letter observable (no se reclama más).
/// Mismo patrón de reclamo que <see cref="IdentityValidationOutboxProcessor"/>:
/// <c>FOR UPDATE SKIP LOCKED</c> por fila en su propia transacción (seguro con varias réplicas),
/// con rama LINQ para providers no relacionales (tests InMemory).
/// </summary>
internal sealed class ProcedureStateChangeOutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcedureStateChangeOutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    /// <summary>Tope de intentos (fuente de verdad en <see cref="ProcedureStateChangeOutbox.MaxDeliveryAttempts"/>).</summary>
    private const int MaxAttempts = ProcedureStateChangeOutbox.MaxDeliveryAttempts;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeño retraso inicial: deja terminar migraciones/seed del arranque antes de sondear.
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                StateChangeOutboxLog.CycleError(logger, ex);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Procesa hasta <see cref="BatchSize"/> filas pendientes (una transacción por fila).</summary>
    internal async Task ProcessPendingAsync(CancellationToken ct)
    {
        for (var i = 0; i < BatchSize; i++)
        {
            if (ct.IsCancellationRequested)
                break;
            var claimed = await ProcessNextClaimedAsync(ct);
            if (!claimed)
                break; // no quedan filas reclamables en este ciclo
        }
    }

    private async Task<bool> ProcessNextClaimedAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

        // El notifier va en un scope PROPIO (DbContext/conexión frescos): su cadena de despacho
        // (OtWebhookDispatchService → OtWebhookSubscriptionRepository.ExecuteInTenantScopeAsync)
        // abre su propia transacción para el SET LOCAL de RLS, y no puede compartir la conexión
        // que sostiene el claim FOR UPDATE de esta fila (InvalidOperationException: "already in
        // a transaction").
        await using var notifierScope = scopeFactory.CreateAsyncScope();
        var notifier = notifierScope.ServiceProvider.GetRequiredService<IProcedureStateChangeNotifier>();

        if (!db.Database.IsRelational())
        {
            // Tests (InMemory): sin transacciones ni locking — reclamo directo por LINQ.
            return await ProcessOneInMemoryAsync(db, notifier, ct);
        }

        // El DbContext usa EnableRetryOnFailure → la transacción manual debe ir dentro de la
        // execution strategy (reintenta el bloque completo ante fallos transitorios de BD).
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => await ProcessOneAsync(db, notifier, ct));
    }

    private async Task<bool> ProcessOneAsync(
        FlitDbContext db, IProcedureStateChangeNotifier notifier, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); // estado limpio en cada (re)intento de la strategy
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimedId = await ClaimNextIdAsync(db, ct);
        if (claimedId is null)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        // La fila está bloqueada por esta transacción (FOR UPDATE); se carga rastreada para sellarla.
        var row = await db.ProcedureStateChangeOutbox.FirstAsync(o => o.Id == claimedId.Value, ct);
        await DispatchAsync(row, notifier, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<bool> ProcessOneInMemoryAsync(
        FlitDbContext db, IProcedureStateChangeNotifier notifier, CancellationToken ct)
    {
        var row = await db.ProcedureStateChangeOutbox
            .Where(o => o.PublishedAt == null && o.Attempts < MaxAttempts)
            .OrderBy(o => o.OccurredAt)
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return false;

        await DispatchAsync(row, notifier, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Despacha UNA fila al notifier y sella <c>published_at</c>; ante fallo deja la fila pendiente
    /// con <c>attempts</c> incrementado (dead-letter observable al agotar los intentos).
    /// </summary>
    private async Task DispatchAsync(
        ProcedureStateChangeOutbox row, IProcedureStateChangeNotifier notifier, CancellationToken ct)
    {
        row.Attempts += 1;
        try
        {
            await notifier.NotifyAsync(
                new ProcedureStateChangeEvent(
                    row.TenantId,
                    row.ProcedureInstanceId,
                    row.FromStatus,
                    row.ToStatus,
                    row.OccurredAt,
                    row.Reason,
                    row.ChangedByUserId),
                ct);

            row.PublishedAt = DateTimeOffset.UtcNow;
            StateChangeOutboxLog.Dispatched(logger, row.ProcedureInstanceId, row.FromStatus, row.ToStatus);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // published_at queda null → reintento en el próximo ciclo, salvo que se agoten los intentos.
            StateChangeOutboxLog.DispatchError(logger, row.ProcedureInstanceId, ex);
            if (row.Attempts >= MaxAttempts)
                StateChangeOutboxLog.DeadLettered(logger, row.ProcedureInstanceId, row.Attempts);
        }
    }

    /// <summary>
    /// Reclama el id de la próxima fila pendiente (con intentos por debajo del tope) usando
    /// <c>FOR UPDATE SKIP LOCKED</c> sobre la conexión/transacción actuales, para que varias
    /// réplicas no tomen la misma fila. SQL crudo: EF Core no expone el locking clause en LINQ.
    /// </summary>
    private static async Task<Guid?> ClaimNextIdAsync(FlitDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT id
            FROM tramites.procedure_state_change_outbox
            WHERE published_at IS NULL
              AND attempts < @max_attempts
            ORDER BY occurred_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """;

        var pMax = cmd.CreateParameter();
        pMax.ParameterName = "max_attempts";
        pMax.Value = MaxAttempts;
        cmd.Parameters.Add(pMax);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetGuid(0) : null;
    }
}

/// <summary>Logging source-generated (CA1848) del procesador de outbox de cambios de estado.</summary>
internal static partial class StateChangeOutboxLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Outbox estados: instancia {ProcedureInstanceId} notificada ({FromStatus} → {ToStatus}).")]
    public static partial void Dispatched(ILogger logger, Guid procedureInstanceId, string? fromStatus, string toStatus);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Outbox estados: error al notificar la instancia {ProcedureInstanceId}; se reintentará.")]
    public static partial void DispatchError(ILogger logger, Guid procedureInstanceId, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Outbox estados: la instancia {ProcedureInstanceId} agotó los reintentos ({Attempts}); queda en dead-letter (NO se reintentará). Requiere revisión manual.")]
    public static partial void DeadLettered(ILogger logger, Guid procedureInstanceId, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Outbox estados: error en el ciclo de sondeo; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
