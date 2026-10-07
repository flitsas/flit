using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>
/// Saca los eventos pendientes de la outbox y los publica (HU #13338). Mismo patrón que la outbox de Trámites
/// (<c>ProcedureStateChangeOutboxProcessor</c>): reclamo con <c>FOR NO KEY UPDATE SKIP LOCKED</c> dentro de una
/// transacción, seguro con varias réplicas. Se sella <c>published_at</c> solo cuando el broker confirmó; si el broker
/// no responde, el evento queda pendiente (sube <c>attempts</c>) y el lote se corta: sale en el siguiente ciclo, en
/// orden, cuando el broker vuelva (AC3). Con el broker (o la base) caído, la espera entre ciclos se duplica en cada
/// fallo seguido hasta <see cref="PlatformMessagingOptions.MaxRetryDelay"/>, y vuelve a <c>PollInterval</c> al primer
/// ciclo sano: una caída larga no llena la base ni el log con un intento por segundo.
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
        var fallosSeguidos = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            CicloOutbox ciclo;
            try
            {
                ciclo = await PublicarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                OutboxLog.CycleError(logger, ex);
                ciclo = new CicloOutbox(0, Fallo: true);
            }

            fallosSeguidos = ciclo.Fallo ? fallosSeguidos + 1 : 0;

            // Lote lleno y sano: puede haber más, se sigue sin esperar.
            if (!ciclo.Fallo && ciclo.Publicados >= options.BatchSize)
                continue;
            try
            {
                await Task.Delay(Espera(fallosSeguidos, options.PollInterval, options.MaxRetryDelay), time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Espera antes del siguiente ciclo: <paramref name="poll"/> si el último salió bien; con fallos seguidos,
    /// <paramref name="poll"/> × 2^(fallos−1) con tope en <paramref name="max"/> (1 s, 2 s, 4 s… 1 min).
    /// </summary>
    internal static TimeSpan Espera(int fallosSeguidos, TimeSpan poll, TimeSpan max)
    {
        if (fallosSeguidos <= 0)
            return poll;
        var exponente = Math.Min(fallosSeguidos - 1, 30);
        var espera = poll * Math.Pow(2, exponente);
        return espera > max ? max : espera;
    }

    /// <summary>Publica un lote. Devuelve cuántos eventos quedaron confirmados.</summary>
    internal async Task<int> PublishPendingAsync(CancellationToken ct) => (await PublicarAsync(ct).ConfigureAwait(false)).Publicados;

    /// <summary>Publica un lote. Devuelve cuántos eventos quedaron confirmados y si el broker falló.</summary>
    internal async Task<CicloOutbox> PublicarAsync(CancellationToken ct)
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
            var fallo = false;
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
                    fallo = true;
                    break; // el broker no responde: el resto del lote espera, en orden
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new CicloOutbox(published, fallo);
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

/// <summary>Resultado de un ciclo del publicador.</summary>
internal readonly record struct CicloOutbox(int Publicados, bool Fallo);

internal static partial class OutboxLog
{
    [LoggerMessage(EventId = 7301, Level = LogLevel.Error, Message = "Outbox: error en el ciclo de publicación")]
    public static partial void CycleError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Warning,
        Message = "Outbox: no se pudo publicar {EventId} ({Type}), intento {Attempts}; queda pendiente")]
    public static partial void PublishFailed(ILogger logger, Guid eventId, string type, int attempts, Exception ex);
}
