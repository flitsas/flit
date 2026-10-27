using Microsoft.EntityFrameworkCore;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>
/// Purga opt-in de la outbox (HU #13727, ADR-0072 R-2): el payload viaja en claro y puede llevar datos personales, así que
/// las filas YA publicadas no se guardan más de <see cref="PlatformMessagingOptions.RetencionPublicados"/>. Corre al
/// arrancar y luego cada hora (o cada retención, si es menor); borra por lotes las filas con <c>published_at</c> anterior a
/// ahora − retención. Las pendientes no se tocan nunca. Solo se registra si la opción tiene valor.
/// </summary>
internal sealed class OutboxPurgeService<TContext>(
    IServiceScopeFactory scopes,
    PlatformMessagingOptions options,
    TimeProvider time,
    ILogger<OutboxPurgeService<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    /// <summary>Filas por sentencia <c>DELETE</c>: lotes cortos para no retener bloqueos largos sobre la outbox.</summary>
    internal const int TamanoLote = 500;

    private static readonly TimeSpan IntervaloMaximo = TimeSpan.FromHours(1);

    private TimeSpan Retencion => options.RetencionPublicados
        ?? throw new InvalidOperationException($"{PlatformMessagingOptions.SectionName}:RetencionPublicados no está configurada.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                OutboxPurgeLog.PurgeError(logger, ex);
            }

            try
            {
                await Task.Delay(Intervalo(Retencion), time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Cada cuánto se purga: cada hora, o cada <paramref name="retencion"/> si es menor.</summary>
    internal static TimeSpan Intervalo(TimeSpan retencion) => retencion < IntervaloMaximo ? retencion : IntervaloMaximo;

    /// <summary>Borra, en lotes de <paramref name="lote"/>, las filas publicadas antes de ahora − retención. Devuelve cuántas.</summary>
    internal async Task<int> PurgarAsync(CancellationToken ct, int lote = TamanoLote)
    {
        var limite = time.GetUtcNow() - Retencion;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var total = 0;
        int borradas;
        do
        {
            borradas = await db.Set<OutboxMessage>()
                .Where(m => m.PublishedAt != null && m.PublishedAt < limite)
                .OrderBy(m => m.PublishedAt)
                .Take(lote)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
            total += borradas;
        }
        while (borradas == lote && !ct.IsCancellationRequested);

        if (total > 0)
            OutboxPurgeLog.Purged(logger, total, limite);
        return total;
    }
}

internal static partial class OutboxPurgeLog
{
    [LoggerMessage(EventId = 7303, Level = LogLevel.Information,
        Message = "Outbox: purgadas {Cantidad} filas publicadas antes de {Limite}")]
    public static partial void Purged(ILogger logger, int cantidad, DateTimeOffset limite);

    [LoggerMessage(EventId = 7304, Level = LogLevel.Error, Message = "Outbox: error en la purga de filas publicadas")]
    public static partial void PurgeError(ILogger logger, Exception ex);
}
