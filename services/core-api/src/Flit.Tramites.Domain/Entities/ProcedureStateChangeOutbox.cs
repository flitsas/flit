namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Outbox transaccional de cambios de estado del trámite (N 03, RNF01 — ADR-0022). Cada fila se
/// persiste en la MISMA unidad de trabajo que la transición de <c>procedure_instances.status</c>
/// (vía <c>ITramiteTransitionPublisher</c>): transición confirmada ⇒ exactamente un evento;
/// rollback ⇒ cero eventos. Un worker en background (<c>ProcedureStateChangeOutboxProcessor</c>)
/// despacha las filas pendientes hacia <c>IProcedureStateChangeNotifier</c> (webhooks OT) y sella
/// <c>published_at</c>. Mismo patrón que <see cref="IdentityValidationOutbox"/>.
/// </summary>
public sealed class ProcedureStateChangeOutbox
{
    /// <summary>
    /// Tope de intentos de entrega del worker. Al alcanzarlo, la fila deja de reclamarse y queda en
    /// DEAD-LETTER (observable: <c>published_at IS NULL AND attempts &gt;= MaxDeliveryAttempts</c>).
    /// Mismo valor que el outbox de identidad.
    /// </summary>
    public const int MaxDeliveryAttempts = 5;

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Instancia de trámite transicionada (procedure_instances.id, referencia lógica).</summary>
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Estado origen (null solo en la creación de la instancia).</summary>
    public string? FromStatus { get; set; }

    /// <summary>Estado destino (vocabulario de negocio, ver <c>TramiteEstado</c>).</summary>
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>Motivo de la transición (RF05); viaja en el payload del webhook OT.</summary>
    public string? Reason { get; set; }

    /// <summary>Usuario que ejecutó la transición; null si fue un proceso automático.</summary>
    public Guid? ChangedByUserId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Marca de despacho (null = pendiente de notificar).</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
