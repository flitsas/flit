using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Procesador de la outbox de validación de identidad (HU #10349, fase 2 — AC4/AC6). Sondea
/// <c>tramites.identity_validation_outbox</c> en busca de eventos <c>completed</c> pendientes
/// (<c>published_at IS NULL</c>), los deserializa y delega en <see cref="IdentityValidationCompletedConsumer"/>
/// (que encadena firma/FUR de los borradores finalizados del sujeto), y sella <c>published_at</c> al
/// terminar. Es el "worker" único para ambos modos de <c>Messaging:IdentityValidation</c>:
/// in-process (default DEV, sin broker) y el stub RabbitMQ — en ambos los eventos quedan en la outbox y
/// este servicio los consume. La firma NUNCA se dispara desde el webhook: el webhook solo encola el
/// evento; aquí ocurre el encadenamiento, de forma idempotente.
/// </summary>
internal sealed class IdentityValidationOutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityValidationOutboxProcessor> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    /// <summary>
    /// Tope de intentos por evento (fuente de verdad en <see cref="IdentityValidationOutbox.MaxDeliveryAttempts"/>).
    /// Al alcanzarlo, la fila deja de reclamarse (dead-letter observable: <c>published_at IS NULL AND
    /// attempts &gt;= MaxAttempts</c>) para no reintentar un veneno por siempre, y queda visible vía la
    /// consulta de eventos atascados.
    /// </summary>
    private const int MaxAttempts = IdentityValidationOutbox.MaxDeliveryAttempts;

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
                OutboxProcessorLog.CycleError(logger, ex);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Procesa hasta <see cref="BatchSize"/> eventos <c>completed</c> pendientes. Cada fila se reclama y
    /// procesa en su PROPIA transacción con <c>FOR UPDATE SKIP LOCKED</c>: así, si core-api corre con
    /// varias réplicas, cada evento lo toma exactamente una de ellas (las demás saltan la fila bloqueada)
    /// → no hay doble-procesamiento al escalar horizontalmente. La transacción por fila acota el tiempo
    /// que se mantiene el lock.
    /// </summary>
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

    /// <summary>
    /// Reclama UNA fila <c>completed</c> pendiente con <c>FOR UPDATE SKIP LOCKED</c> (no bloqueante: si otra
    /// réplica ya la tomó, la salta), la procesa vía el consumer y sella <c>published_at</c>. Ante fallo,
    /// deja <c>published_at</c> en null e incrementa <c>attempts</c> (los handlers de firma/FUR son
    /// idempotentes, así que reprocesar es seguro); al alcanzar <see cref="MaxAttempts"/> deja de
    /// reclamarse (dead-letter observable). Devuelve <c>false</c> si no había ninguna fila reclamable.
    /// </summary>
    private async Task<bool> ProcessNextClaimedAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var consumer = scope.ServiceProvider.GetRequiredService<IdentityValidationCompletedConsumer>();

        // El DbContext usa EnableRetryOnFailure → la transacción manual debe ir dentro de la execution
        // strategy (reintenta el bloque completo ante fallos transitorios de BD).
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => await ProcessOneAsync(db, consumer, ct));
    }

    private async Task<bool> ProcessOneAsync(
        FlitDbContext db, IdentityValidationCompletedConsumer consumer, CancellationToken ct)
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
        var row = await db.IdentityValidationOutbox.FirstAsync(o => o.Id == claimedId.Value, ct);
        row.Attempts += 1;
        try
        {
            var evt = JsonSerializer.Deserialize<IdentityValidationCompleted>(row.Payload, JsonOptions);
            if (evt is not null)
            {
                var result = await consumer.HandleAsync(evt, ct);
                OutboxProcessorLog.Dispatched(logger, row.ValidationId, result.Matched, result.Processed);
            }

            row.PublishedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // published_at queda null → reintento en el próximo ciclo, salvo que se agoten los intentos.
            OutboxProcessorLog.DispatchError(logger, row.ValidationId, ex);
            if (row.Attempts >= MaxAttempts)
                OutboxProcessorLog.DeadLettered(logger, row.ValidationId, row.Attempts);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Reclama el id de la próxima fila <c>completed</c> pendiente (con intentos por debajo del tope) usando
    /// <c>FOR UPDATE SKIP LOCKED</c> sobre la conexión/transacción actuales, para que varias réplicas no
    /// tomen la misma fila. SQL crudo: EF Core no expone el locking clause en LINQ.
    /// </summary>
    private static async Task<Guid?> ClaimNextIdAsync(FlitDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT id
            FROM tramites.identity_validation_outbox
            WHERE published_at IS NULL
              AND event_type = @event_type
              AND attempts < @max_attempts
            ORDER BY occurred_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """;

        var pType = cmd.CreateParameter();
        pType.ParameterName = "event_type";
        pType.Value = IdentityValidationEventTypes.Completed;
        cmd.Parameters.Add(pType);

        var pMax = cmd.CreateParameter();
        pMax.ParameterName = "max_attempts";
        pMax.Value = MaxAttempts;
        cmd.Parameters.Add(pMax);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetGuid(0) : null;
    }
}

/// <summary>Logging source-generated (CA1848) del procesador de outbox de identidad.</summary>
internal static partial class OutboxProcessorLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Outbox identidad: validación {ValidationId} procesada (coincidencias={Matched}, encadenadas={Processed}).")]
    public static partial void Dispatched(ILogger logger, Guid validationId, int matched, int processed);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Outbox identidad: error al despachar la validación {ValidationId}; se reintentará.")]
    public static partial void DispatchError(ILogger logger, Guid validationId, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Outbox identidad: validación {ValidationId} agotó los reintentos ({Attempts}); queda en dead-letter (NO se reintentará). Requiere revisión manual.")]
    public static partial void DeadLettered(ILogger logger, Guid validationId, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Outbox identidad: error en el ciclo de sondeo; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
