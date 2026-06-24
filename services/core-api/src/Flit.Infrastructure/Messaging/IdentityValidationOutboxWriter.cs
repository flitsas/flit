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
        var outbox = new IdentityValidationOutbox
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            ValidationId = evt.ValidationId,
            EventType = evt.EventType,
            Payload = JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions),
            OccurredAt = now,
            PublishedAt = now,
            Attempts = 1,
            CreatedAt = now,
        };

        // Added explícito: la PK es store-generated (uuidv7()) pero se asigna en código.
        db.Add(outbox);
        return outbox;
    }
}
