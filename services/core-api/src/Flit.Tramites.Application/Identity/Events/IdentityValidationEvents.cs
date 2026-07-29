namespace Flit.Tramites.Application.Identity.Events;

/// <summary>
/// Contrato base de los eventos de validación de identidad (HU #10233, fase 2 event-driven). Son
/// contratos PÚBLICOS y SANITIZADOS: solo ids/estado/proveedor, sin PII cruda ni secretos. En fase 1 se
/// persisten en la outbox y se despachan in-process; en fase 2 un worker los publicará a RabbitMQ con
/// esta misma forma (no cambian).
/// </summary>
public abstract record IdentityValidationEvent
{
    /// <summary>Tipo de evento (<see cref="Domain.Entities.IdentityValidationEventTypes"/>).</summary>
    public abstract string EventType { get; }

    public required Guid TenantId { get; init; }

    /// <summary>
    /// HU #10865 — nullable para prevalidaciones standalone (sin trámite previo).
    /// Null cuando la validación biométrica no está ligada a ninguna instancia de trámite.
    /// </summary>
    public Guid? ProcedureInstanceId { get; init; }

    public required Guid ValidationId { get; init; }

    /// <summary>'mock' | 'kyverum'.</summary>
    public required string Provider { get; init; }

    /// <summary>Parte del trámite: comprador|vendedor|null.</summary>
    public string? Parte { get; init; }
}

/// <summary>Se solicitó la validación de identidad al proveedor (captura iniciada). AC1.</summary>
public sealed record IdentityValidationRequested : IdentityValidationEvent
{
    public override string EventType => Domain.Entities.IdentityValidationEventTypes.Requested;

    /// <summary>Id de la verificación en el proveedor (correlación con el webhook).</summary>
    public string? ProviderVerificationId { get; init; }
}

/// <summary>El proveedor resolvió la validación (aprobado|rechazado) vía webhook. AC2.</summary>
public sealed record IdentityValidationCompleted : IdentityValidationEvent
{
    public override string EventType => Domain.Entities.IdentityValidationEventTypes.Completed;

    /// <summary>Estado FLIT resuelto: aprobado|rechazado (ver <c>BiometricEstados</c>).</summary>
    public required string Estado { get; init; }

    /// <summary>Estado crudo del proveedor (p.ej. approved|rejected).</summary>
    public string? ProviderStatus { get; init; }

    public int? Score { get; init; }
}
