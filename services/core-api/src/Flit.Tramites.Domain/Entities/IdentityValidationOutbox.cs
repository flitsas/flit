namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Outbox transaccional de eventos de validación de identidad (HU #10233 — fase 2 event-driven).
/// Cada fila representa un evento de dominio (identity_validation.requested|completed) que se persiste
/// en la MISMA transacción que el cambio de estado de la validación (patrón outbox), garantizando que
/// el evento no se pierde si el broker no está disponible. En fase 1 el despacho es in-process
/// (<c>InProcessIdentityValidationEventDispatcher</c>); en fase 2 un worker lo publicará a RabbitMQ.
/// El <see cref="Payload"/> va SANITIZADO (sin PII cruda ni secretos).
/// </summary>
public sealed class IdentityValidationOutbox
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Validación biométrica de origen (procedure_instance_biometric_validations.id).</summary>
    public Guid ValidationId { get; set; }

    /// <summary>Tipo de evento: ver <see cref="IdentityValidationEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Cuerpo del evento (jsonb) sanitizado: ids, estado, proveedor — sin PII ni secretos.</summary>
    public string Payload { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Marca de despacho (null = pendiente). En fase 1 se sella al despachar in-process.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tipos de evento de validación de identidad (HU #10233, contratos fase 2).</summary>
public static class IdentityValidationEventTypes
{
    /// <summary>Se solicitó la validación de identidad al proveedor (captura iniciada).</summary>
    public const string Requested = "identity_validation.requested";

    /// <summary>El proveedor resolvió la validación (aprobado|rechazado) vía webhook.</summary>
    public const string Completed = "identity_validation.completed";
}
