using System.Text.Json;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Encola un evento de validación de identidad en la outbox usando el <see cref="FlitDbContext"/> scoped
/// del handler (sin <c>SaveChanges</c>: se confirma con la unidad de trabajo del caso de uso — outbox
/// transaccional, HU #10233). Compartido por los publishers in-process y RabbitMQ (stub).
/// </summary>
internal static class IdentityValidationOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IdentityValidationOutbox Enqueue(FlitDbContext db, IdentityValidationEvent evt)
    {
        var now = DateTimeOffset.UtcNow;

        // HU #10349 (fase 2): los eventos 'completed' se dejan PENDIENTES (published_at = null) para que
        // el procesador de outbox encadene el auto-flujo (firma/FUR) de los borradores finalizados y luego
        // selle published_at. Los 'requested' no tienen consumidor → se sellan al despachar (in-process).
        var pending = string.Equals(
            evt.EventType, IdentityValidationEventTypes.Completed, StringComparison.Ordinal);

        var outbox = new IdentityValidationOutbox
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            ValidationId = evt.ValidationId,
            EventType = evt.EventType,
            Payload = JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions),
            OccurredAt = now,
            PublishedAt = pending ? null : now,
            Attempts = pending ? 0 : 1,
            CreatedAt = now,
        };

        // Added explícito: la PK es store-generated (uuidv7()) pero se asigna en código.
        db.Add(outbox);
        return outbox;
    }
}
