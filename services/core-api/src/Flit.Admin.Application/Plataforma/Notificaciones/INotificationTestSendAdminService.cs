namespace Flit.Admin.Application.Plataforma.Notificaciones;

/// <summary>
/// Petición de envío de prueba (HU #11368, Feature #11349).
/// </summary>
/// <param name="TemplateId">Id ESTABLE de la plantilla del catálogo (ver <c>NotificationTemplateCatalog</c>).</param>
/// <param name="Channel">
/// Código wire del canal: <c>FLIT_SMTP</c> | <c>TENANT_API</c> (ver <c>SettingsWire</c>). Ninguno
/// de los dos infiere el canal: lo elige el SuperAdmin en cada envío.
/// </param>
public sealed record SendNotificationTestRequest(string? TemplateId, string? Channel);

/// <summary>
/// HU #11368 AC1-AC8 — catálogo CERRADO de desenlaces de un intento de envío de prueba.
/// <see cref="Sent"/> es la única causa de éxito.
/// </summary>
public enum NotificationTestSendOutcome
{
    /// <summary>Éxito: el intento de envío se completó (AC1). Ver <see cref="NotificationTestSendResult.IsConsoleTransport"/>.</summary>
    Sent,

    /// <summary>AC6 — el id de plantilla no existe en el catálogo.</summary>
    TemplateNotFound,

    /// <summary>Canal fuera del catálogo (<c>FLIT_SMTP</c> | <c>TENANT_API</c>).</summary>
    InvalidChannel,

    /// <summary>AC3 — el banco de pruebas no tiene buzón configurado.</summary>
    MailboxNotConfigured,

    /// <summary>AC2 — envío exitoso hace menos de la ventana de enfriamiento.</summary>
    RateLimited,

    /// <summary>
    /// AC4 (desviación deliberada respecto al literal "canal sin adaptador" — ver
    /// <c>NotificationTestSendAdminService</c>) — el canal elegido no tiene remitente/configuración
    /// utilizable en este ambiente.
    /// </summary>
    ChannelNotConfigured,

    /// <summary>AC5 — el render de la muestra de la plantilla falló.</summary>
    RenderFailed,

    /// <summary>
    /// El intento de envío llegó al transporte (SMTP/consola) pero el transporte reportó un fallo
    /// (ver el catálogo cerrado <c>EmailSendOutcome</c> del puerto <c>IEmailSender</c>).
    /// </summary>
    TransportFailed,
}

/// <summary>
/// Resultado de un intento de envío de prueba (HU #11368).
/// </summary>
/// <param name="Success"><c>true</c> únicamente cuando <see cref="Outcome"/> es <see cref="NotificationTestSendOutcome.Sent"/>.</param>
/// <param name="Outcome">Causa cerrada del desenlace.</param>
/// <param name="Message">Texto genérico para mostrar al SuperAdmin — nunca datos sensibles.</param>
/// <param name="TemplateId">Id de la plantilla solicitada, cuando se pudo resolver.</param>
/// <param name="Channel">Canal wire solicitado, cuando es válido.</param>
/// <param name="SenderEmail">Remitente resuelto del canal (AC1), o <c>null</c> si no aplica.</param>
/// <param name="SenderName">Nombre remitente resuelto del canal (AC1), o <c>null</c> si no aplica.</param>
/// <param name="SentAt">Instante del intento sellado en <c>last_test_sent_at</c> (AC1), o <c>null</c> si no se selló.</param>
/// <param name="RetryAfterSeconds">AC2 — segundos restantes de la ventana de enfriamiento, solo con <see cref="NotificationTestSendOutcome.RateLimited"/>.</param>
/// <param name="IsConsoleTransport">
/// AC8 — <c>true</c> cuando el transporte activo fue de consola: ningún correo real salió del
/// proceso, solo quedó una línea en el log del servidor.
/// </param>
public sealed record NotificationTestSendResult(
    bool Success,
    NotificationTestSendOutcome Outcome,
    string Message,
    string? TemplateId,
    string? Channel,
    string? SenderEmail,
    string? SenderName,
    DateTimeOffset? SentAt,
    int? RetryAfterSeconds,
    bool IsConsoleTransport)
{
    public static NotificationTestSendResult Failure(
        NotificationTestSendOutcome outcome,
        string message,
        string? templateId = null,
        string? channel = null,
        int? retryAfterSeconds = null) =>
        new(
            Success: false,
            Outcome: outcome,
            Message: message,
            TemplateId: templateId,
            Channel: channel,
            SenderEmail: null,
            SenderName: null,
            SentAt: null,
            RetryAfterSeconds: retryAfterSeconds,
            IsConsoleTransport: false);
}

/// <summary>
/// Envío de prueba de una plantilla del catálogo al buzón de pruebas, con límite de frecuencia
/// persistido (HU #11368, Feature #11349). Alcance EXACTO de esta HU: lee/sella
/// <c>admin.notification_test_settings</c> (HU #11365/#11366) y resuelve el remitente por canal
/// (HU #11367) — nunca construye un adaptador de transporte nuevo.
/// </summary>
public interface INotificationTestSendAdminService
{
    Task<NotificationTestSendResult> SendAsync(
        SendNotificationTestRequest request, Guid? userId, CancellationToken ct = default);
}
