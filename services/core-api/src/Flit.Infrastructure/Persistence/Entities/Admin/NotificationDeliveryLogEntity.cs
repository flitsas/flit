namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Fila de <c>admin.notification_delivery_logs</c> — bitácora append-only de intentos de envío de
/// notificación (HU #11363, Feature #11348). El esquema (DDL 64) es de la HU #11357; esta entidad
/// solo mapea las columnas para que la HU #11363 pueda escribir (decorador) y leer (consulta
/// tenant-scoped) la fila.
/// </summary>
internal sealed class NotificationDeliveryLogEntity
{
    public Guid Id { get; set; }

    /// <summary>NOT NULL en BD. Ver <c>NotificationDeliveryLoggingEmailSender</c>: un envío sin
    /// tenant resoluble (recuperación de contraseña sin rol activo) NUNCA llega a insertarse aquí.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Id estable de la plantilla (<c>NotificationTemplateCatalog.TemplateIds</c>).</summary>
    public string TemplateKey { get; set; } = string.Empty;

    /// <summary>Canal efectivo del envío. Hoy siempre <c>"flit_smtp"</c> — el enrutamiento por
    /// canal (HU #11362) todavía no existe.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>@pii:medium — correo del destinatario. Finalidad: auditoría de envíos (Ley 1581).</summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary><c>enviado</c> | <c>fallido</c>.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Causa genérica del fallo (<see cref="Flit.Modules.Security.Domain.Auth.EmailSendResult.Message"/>).
    /// <c>null</c> cuando <see cref="Result"/> es <c>enviado</c>.</summary>
    public string? FailureReason { get; set; }

    public int DurationMs { get; set; }

    /// <summary>HU #11364 AC2 — <c>true</c> cuando el adaptador de canal sustituyó el destinatario
    /// real por uno de desvío (fuera de producción) antes de enviar. Con <c>true</c>, el envío NO
    /// llegó a <see cref="Recipient"/> (que sigue guardando el destinatario ORIGINAL).</summary>
    public bool RecipientDiverted { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
