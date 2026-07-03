using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Publisher de eventos de validación de identidad por defecto (HU #10233, fase 1). Encola el evento en
/// la outbox (<c>tramites.identity_validation_outbox</c>) usando el MISMO <see cref="FlitDbContext"/>
/// scoped que el handler: la fila se confirma en el <c>SaveChanges</c> del caso de uso (outbox
/// transaccional). El despacho es in-process (log). En fase 2 un worker leerá la outbox y publicará a
/// RabbitMQ (ver <see cref="RabbitMqIdentityValidationEventPublisher"/>).
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
        Message = "[STUB RabbitMQ] Se publicaría {EventType} para validación {ValidationId} (fase 2 — no implementado).")]
    public static partial void RabbitMqStub(ILogger logger, string eventType, Guid validationId);
}
