using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Infrastructure.Persistence;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Implementación real del puerto de publicación de transiciones (N 03 RNF01, ADR-0022):
/// encola el cambio de estado en <c>tramites.procedure_state_change_outbox</c> usando el
/// <see cref="FlitDbContext"/> scoped del caso de uso, SIN <c>SaveChanges</c> — la fila se
/// confirma con la unidad de trabajo del <c>ITramiteLifecycleService</c> (outbox transaccional):
/// transición confirmada ⇒ exactamente un evento; rollback ⇒ cero eventos. La entrega efectiva
/// la hace <see cref="ProcedureStateChangeOutboxProcessor"/> tras el commit.
/// </summary>
internal sealed class ProcedureStateChangeOutboxPublisher(FlitDbContext db) : ITramiteTransitionPublisher
{
    public Task EnqueueAsync(TramiteTransitionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var now = DateTimeOffset.UtcNow;

        // Added explícito: la PK es store-generated (uuidv7()) pero se asigna en código,
        // igual que IdentityValidationOutboxWriter.
        db.Add(new ProcedureStateChangeOutbox
        {
            Id = Guid.NewGuid(),
            TenantId = record.TenantId,
            ProcedureInstanceId = record.ProcedureInstanceId,
            FromStatus = record.FromStatus,
            ToStatus = record.ToStatus,
            Reason = record.Reason,
            ChangedByUserId = record.ChangedByUserId,
            OccurredAt = record.ChangedAt,
            PublishedAt = null,
            Attempts = 0,
            CreatedAt = now,
        });

        return Task.CompletedTask;
    }
}
