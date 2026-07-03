using System.Text.Json;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Encola un evento de validación de identidad en la outbox usando el <see cref="FlitDbContext"/> scoped
/// del handler (sin <c>SaveChanges</c>: se confirma con la unidad de trabajo del caso de uso — outbox
/// transaccional, HU #10233). Compartido por los publishers in-process y RabbitMQ (stub).
/// </summary>
internal static class IdentityValidationOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IdentityValidationOutbox?> EnqueueAsync(
        FlitDbContext db, IdentityValidationEvent evt, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // HU #10349 (fase 2): los eventos 'completed' se dejan PENDIENTES (published_at = null) para que
        // el procesador de outbox encadene el auto-flujo (firma/FUR) de los borradores finalizados y luego
        // selle published_at. Los 'requested' no tienen consumidor → se sellan al despachar (in-process).
        var pending = string.Equals(
            evt.EventType, IdentityValidationEventTypes.Completed, StringComparison.Ordinal);

        // IDEMPOTENCIA a nivel outbox: a lo sumo un 'completed' por validación (índice único parcial
        // uq_identity_validation_outbox_completed). Si ya existe uno —p. ej. una validación que se
        // re-terminaliza tras reabrirse fuera de banda, o un reproceso— NO se encola otro: encolarlo
        // violaría el índice (23505) y, al compartir el SaveChanges del caso de uso, abortaría TODA la
        // unidad de trabajo (estado + intentos + evento), dejando la validación atascada en 500. Skippear
        // aquí deja que el cambio de estado se persista limpio (el evento ya fue emitido una vez).
        if (pending)
        {
            var yaEncolado =
                db.ChangeTracker.Entries<IdentityValidationOutbox>().Any(e =>
                    e.State is not EntityState.Deleted
                    && e.Entity.ValidationId == evt.ValidationId
                    && string.Equals(e.Entity.EventType, IdentityValidationEventTypes.Completed, StringComparison.Ordinal))
                || await db.Set<IdentityValidationOutbox>().AnyAsync(
                    x => x.ValidationId == evt.ValidationId
                        && x.EventType == IdentityValidationEventTypes.Completed, ct);
            if (yaEncolado)
                return null;
        }

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
