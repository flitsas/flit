namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Fila de <c>tramites.procedure_state_change_email_dispatches</c> — cola de despachos de
/// correo al cambio de estado del trámite (HU #11461, Feature #11459, ADR-0045).
/// <para>
/// El esquema (DDL 69) vive en SQL embebido; esta entidad mapea columnas para que el sink
/// (HU #11465) y el worker (HU #11467) lean/escriban sin tocar el scaffolding de EF para los
/// índices expresión (<c>lower(recipient)</c>) ni la RLS.
/// </para>
/// </summary>
public sealed class ProcedureStateChangeEmailDispatch
{
    public const int MaxDeliveryAttempts = 5;

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OutboxId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>@pii:medium — correo del destinatario. Null = cupo omitido.</summary>
    public string? Recipient { get; set; }

    public string? RecipientName { get; set; }

    /// <summary>Rol en el trámite (comprador | vendedor).</summary>
    public string RecipientRole { get; set; } = string.Empty;

    /// <summary>persona | empresa | representante_legal.</summary>
    public string RecipientKind { get; set; } = string.Empty;

    public string TemplateKey { get; set; } = string.Empty;

    /// <summary>pendiente | enviado | fallido | omitido.</summary>
    public string Status { get; set; } = string.Empty;

    public string? FailureReason { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
