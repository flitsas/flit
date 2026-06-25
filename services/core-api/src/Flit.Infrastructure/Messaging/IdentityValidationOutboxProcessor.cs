using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
    /// Procesa un lote de eventos <c>completed</c> pendientes. Cada fila se sella (<c>published_at</c>)
    /// solo si el consumo no lanza; ante error queda pendiente y se reintenta en el próximo ciclo
    /// (los handlers de firma/FUR son idempotentes, así que reprocesar es seguro).
    /// </summary>
    internal async Task ProcessPendingAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var consumer = scope.ServiceProvider.GetRequiredService<IdentityValidationCompletedConsumer>();

        var pending = await db.IdentityValidationOutbox
            .Where(o => o.PublishedAt == null
                && o.EventType == IdentityValidationEventTypes.Completed)
            .OrderBy(o => o.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var row in pending)
        {
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
                // published_at queda null → reintento en el próximo ciclo.
                OutboxProcessorLog.DispatchError(logger, row.ValidationId, ex);
            }

            await db.SaveChangesAsync(ct);
        }
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

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Outbox identidad: error en el ciclo de sondeo; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
