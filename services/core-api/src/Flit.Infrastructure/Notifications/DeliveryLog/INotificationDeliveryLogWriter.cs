namespace Flit.Infrastructure.Notifications.DeliveryLog;

/// <summary>
/// Un intento de envío ya resuelto (éxito o fallo), listo para registrar en
/// <c>admin.notification_delivery_logs</c> (HU #11363). <see cref="TenantId"/> es <b>no nulo</b> a
/// propósito: la decisión de qué hacer con un <c>EmailMessage.TenantId</c> nulo la toma
/// <c>NotificationDeliveryLoggingEmailSender</c> ANTES de construir esta entrada (Decisión A —
/// no se escribe fila; ver comentario allí), nunca este escritor.
/// </summary>
internal sealed record NotificationDeliveryLogEntry(
    Guid TenantId,
    string TemplateKey,
    string Channel,
    string Recipient,
    bool Success,
    string? FailureReason,
    int DurationMs,
    bool RecipientDiverted = false)
{
    /// <summary>HU #12428 AC5 — <c>flit</c> | <c>brand</c> aplicado a este envío. <c>null</c> si el
    /// composer no participó de la resolución de tema.</summary>
    public string? ThemeKind { get; init; }

    /// <summary><c>tenant_brandings.published_version</c> aplicada (solo <c>brand</c>).</summary>
    public int? ThemeVersion { get; init; }

    /// <summary>HU #12430 AC5 — nombre visible del remitente aplicado.</summary>
    public string? SenderName { get; init; }

    /// <summary>HU #12430 AC5 — dirección de remitente aplicada.</summary>
    public string? SenderEmail { get; init; }
}

/// <summary>
/// Escribe una fila de la bitácora de envíos. Implementación única: <see cref="NotificationDeliveryLogWriter"/>.
/// Existe como interfaz (y no una llamada directa a <c>FlitDbContext</c> desde el decorador) para que
/// el decorador pueda resolverla en un <c>IServiceScope</c> PROPIO — un <c>FlitDbContext</c> nuevo,
/// aislado del que sostiene el resto de la petición — sin acoplarse a los detalles de EF Core.
/// </summary>
internal interface INotificationDeliveryLogWriter
{
    Task WriteAsync(NotificationDeliveryLogEntry entry, CancellationToken cancellationToken);
}
