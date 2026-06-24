using Flit.Tramites.Application.Identity.Events;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Publica eventos de validación de identidad (HU #10233, fase 2). El publisher por defecto
/// (<c>InProcessIdentityValidationEventDispatcher</c>) ENCOLA el evento en la outbox usando el mismo
/// DbContext del handler — la fila se confirma en el <c>SaveChanges</c> del caso de uso (patrón outbox
/// transaccional), por lo que <b>no</b> debe llamar a SaveChanges por su cuenta. En fase 2 un stub
/// (<c>RabbitMqIdentityValidationEventPublisher</c>) publicará además al broker.
/// </summary>
public interface IIdentityValidationEventPublisher
{
    /// <summary>Encola el evento (outbox) dentro de la unidad de trabajo del handler.</summary>
    Task PublishAsync(IdentityValidationEvent evt, CancellationToken ct = default);
}
