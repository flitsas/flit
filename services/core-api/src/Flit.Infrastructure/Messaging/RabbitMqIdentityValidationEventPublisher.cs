using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk.Messaging;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #13350 (antes stub de la HU #10233): encola el evento en la outbox propia, de la que sigue saliendo el
/// auto-flujo de firma/FUR de los 'completed', y lo publica en <c>flit.tramites</c>
/// (<c>tramites.identity_validation.*</c>) por la outbox del SDK, ambos en la unidad de trabajo del caso de uso.
/// Se registra con el bus de Trámites encendido (<see cref="TramitesBusOptions.Habilitado"/>).
/// </summary>
internal sealed class RabbitMqIdentityValidationEventPublisher(
    FlitDbContext db,
    IPlatformOutbox bus,
    ILogger<RabbitMqIdentityValidationEventPublisher> logger) : IIdentityValidationEventPublisher
{
    public async Task PublishAsync(IdentityValidationEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        // null = 'completed' ya encolado antes para esta validación: el evento ya salió una vez.
        if (await IdentityValidationOutboxWriter.EnqueueAsync(db, evt, ct) is null)
            return;

        var sobre = bus.Enqueue(TramitesEventos.DeValidacion(evt), 1, evt.TenantId, TramitesEventos.Datos(evt));
        IdentityValidationLog.EnqueuedToBus(logger, sobre.Type, evt.ValidationId, sobre.EventId);
    }
}
