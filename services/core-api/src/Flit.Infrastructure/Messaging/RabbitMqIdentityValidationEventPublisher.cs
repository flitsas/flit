using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// STUB de publicación a RabbitMQ (HU #10233, contratos fase 2). NO publica a ningún broker: encola el
/// evento en la outbox (igual que el dispatcher in-process, para no perder el evento) y registra un log
/// indicando lo que se publicaría. Se cableará a un broker real + worker lector de la outbox en la fase
/// 2 del Feature #10235. No se registra por defecto: se activa con <c>Messaging:IdentityValidation=rabbitmq</c>.
/// </summary>
internal sealed class RabbitMqIdentityValidationEventPublisher(
    FlitDbContext db,
    ILogger<RabbitMqIdentityValidationEventPublisher> logger) : IIdentityValidationEventPublisher
{
    public async Task PublishAsync(IdentityValidationEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        await IdentityValidationOutboxWriter.EnqueueAsync(db, evt, ct);
        IdentityValidationLog.RabbitMqStub(logger, evt.EventType, evt.ValidationId);
    }
}
