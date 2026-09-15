namespace Flit.Tramites.Domain.RevocationRequests;

/// <summary>
/// Hitos del sub-flujo de revocatoria que disparan correo (HU #12579, Feature #12565, AC1).
/// Reflejan el CHECK de BD (<c>ck_rre_dispatches_milestone</c>).
/// </summary>
public static class RevocationRequestEmailMilestone
{
    /// <summary>Solicitud de revocatoria recibida (acuse al radicador).</summary>
    public const string Solicitada = "solicitada";

    /// <summary>Decisión del organismo de tránsito: aprobada (trámite queda Revocado).</summary>
    public const string Aprobada = "aprobada";

    /// <summary>Decisión del organismo de tránsito: rechazada (el gestor puede reintentar).</summary>
    public const string Rechazada = "rechazada";
}

/// <summary>
/// Fila de <c>tramites.revocation_request_email_dispatches</c> — cola de despachos de correo por
/// hito del sub-flujo de revocatoria (HU #12579, Feature #12565, ADR-0046 Opción B extendido).
/// <para>
/// El esquema (DDL 116) vive en SQL embebido; esta entidad mapea columnas para que el sink
/// (<c>RevocationRequestNotificationEnqueuer</c>) y el worker
/// (<c>RevocationRequestEmailDispatchProcessor</c>) lean/escriban sin tocar el scaffolding de EF
/// para los índices expresión (<c>lower(recipient)</c>) ni la RLS.
/// </para>
/// <para>
/// Gemela de <see cref="Entities.PlateAssignmentEmailDispatch"/> con la idempotencia anclada a
/// <c>(revocation_request_id, milestone)</c> en vez de <c>(procedure_instance_id, plate)</c> — ver
/// XML doc del DDL.
/// </para>
/// </summary>
public sealed class RevocationRequestEmailDispatch
{
    public const int MaxDeliveryAttempts = 5;

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public Guid RevocationRequestId { get; set; }

    /// <summary>Denormalizado del padre — solo trazabilidad, no participa en la idempotencia.</summary>
    public int AttemptNumber { get; set; }

    /// <summary>solicitada | aprobada | rechazada — ver <see cref="RevocationRequestEmailMilestone"/>.</summary>
    public string Milestone { get; set; } = string.Empty;

    /// <summary>@pii:medium — correo del destinatario. Null = cupo omitido.</summary>
    public string? Recipient { get; set; }

    public string? RecipientName { get; set; }

    /// <summary>Rol en el trámite. Hoy únicamente <c>radicador</c>.</summary>
    public string RecipientRole { get; set; } = string.Empty;

    /// <summary>persona | empresa | representante_legal.</summary>
    public string RecipientKind { get; set; } = string.Empty;

    public string TemplateKey { get; set; } = string.Empty;

    /// <summary>pendiente | enviado | fallido | omitido.</summary>
    public string Status { get; set; } = string.Empty;

    public string? FailureReason { get; set; }

    /// <summary>Motivo de rechazo denormalizado del padre. Solo <see cref="RevocationRequestEmailMilestone.Rechazada"/>.</summary>
    public string? DecisionReason { get; set; }

    public int Attempts { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
