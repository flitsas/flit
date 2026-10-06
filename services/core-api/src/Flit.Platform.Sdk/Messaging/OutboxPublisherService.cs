using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>
/// Saca los eventos pendientes de la outbox y los publica (HU #13338). Mismo patrón que la outbox de Trámites
/// (<c>ProcedureStateChangeOutboxProcessor</c>): reclamo con <c>FOR NO KEY UPDATE SKIP LOCKED</c> dentro de una
/// transacción, seguro con varias réplicas. Se sella <c>published_at</c> solo cuando el broker confirmó; si el broker
/// no responde, el evento queda pendiente (sube <c>attempts</c>) y el lote se corta: sale en el siguiente ciclo, en
/// orden, cuando el broker vuelva (AC3).
/// </summary>
internal sealed class OutboxPublisherService<TContext>(
    IServiceScopeFactory scopes,
    IEventPublisher publisher,
    PlatformMessagingOptions options,
    TimeProvider time,
    ILogger<OutboxPublisherService<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int published;
            try
            {
                published = await PublishPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                OutboxLog.CycleError(logger, ex);
                published = 0;
            }

            // Lote lleno: puede haber más, se sigue sin esperar.
            if (published >= options.BatchSize)
                continue;
            try
            {
                await Task.Delay(options.PollInterval, time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Publica un lote. Devuelve cuántos eventos quedaron confirmados.</summary>
    internal async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            // La tabla sale del modelo de EF (no de una entrada externa); el tamaño del lote va como parámetro.
            var sql = "SELECT * FROM " + Table(db) + " WHERE published_at IS NULL ORDER BY occurred_at, id LIMIT {0} FOR NO KEY UPDATE SKIP LOCKED";
            var batch = await db.Set<OutboxMessage>().FromSqlRaw(sql, options.BatchSize).ToListAsync(ct).ConfigureAwait(false);

            var published = 0;
            foreach (var message in batch)
            {
                try
                {
                    await publisher.PublishAsync(message, ct).ConfigureAwait(false);
                    message.PublishedAt = time.GetUtcNow();
                    message.LastError = null;
                    published++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    message.Attempts++;
                    message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                    OutboxLog.PublishFailed(logger, message.Id, message.RoutingKey, message.Attempts, ex);
                    break; // el broker no responde: el resto del lote espera, en orden
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return published;
        }).ConfigureAwait(false);
    }

    private static string Table(DbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException("El DbContext no mapea la outbox: falta modelBuilder.AddFlitOutbox(schema).");
        var schema = entity.GetSchema();
        var table = entity.GetTableName();
        return schema is null ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
    }
}

internal static partial class OutboxLog
{
    [LoggerMessage(EventId = 7301, Level = LogLevel.Error, Message = "Outbox: error en el ciclo de publicación")]
    public static partial void CycleError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Warning,
        Message = "Outbox: no se pudo publicar {EventId} ({Type}), intento {Attempts}; queda pendiente")]
    public static partial void PublishFailed(ILogger logger, Guid eventId, string type, int attempts, Exception ex);
}
