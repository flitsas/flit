using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Publisher de eventos de validación de identidad por defecto (HU #10233, fase 1). Encola el evento en
/// la outbox (<c>tramites.identity_validation_outbox</c>) usando el MISMO <see cref="FlitDbContext"/>
/// scoped que el handler: la fila se confirma en el <c>SaveChanges</c> del caso de uso (outbox
/// transaccional). El despacho es in-process (log). Con el bus de Trámites encendido se usa
/// <see cref="RabbitMqIdentityValidationEventPublisher"/>, que además lo publica a RabbitMQ (HU #13350).
/// </summary>
internal sealed class InProcessIdentityValidationEventDispatcher(
    FlitDbContext db,
    ILogger<InProcessIdentityValidationEventDispatcher> logger) : IIdentityValidationEventPublisher
{
    public async Task PublishAsync(IdentityValidationEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        await IdentityValidationOutboxWriter.EnqueueAsync(db, evt, ct);
        IdentityValidationLog.EventDispatched(logger, evt.EventType, evt.ValidationId);
    }
}

/// <summary>Logging source-generated (CA1848) para el despacho de eventos de validación de identidad.</summary>
internal static partial class IdentityValidationLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Evento de validación de identidad encolado (in-process): {EventType} para validación {ValidationId}")]
    public static partial void EventDispatched(ILogger logger, string eventType, Guid validationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Evento de validación de identidad encolado al bus: {EventType} para validación {ValidationId} (evento {EventId})")]
    public static partial void EnqueuedToBus(ILogger logger, string eventType, Guid validationId, Guid eventId);
}
