namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Mensaje de correo listo para enviar (cuerpo HTML ya compuesto).
/// </summary>
/// <param name="TenantId">
/// HU #11358 AC1 — tenant explícito de la solicitud. Quien construye el mensaje DEBE
/// suministrarlo (parámetro posicional, sin valor por defecto); ninguna implementación de
/// transporte (<see cref="Flit.Infrastructure.Email.SmtpEmailSender"/> ni
/// <see cref="Flit.Infrastructure.Email.ConsoleEmailSender"/>, ver comentario allí) lo infiere
/// del contexto de request. Es <c>Guid?</c> porque hay un caso legítimo sin tenant resoluble:
/// recuperación de contraseña de un usuario sin ninguna asignación de rol activa (ver
/// <see cref="PasswordRecoveryUser"/>) — el valor null sigue siendo "suministrado
/// explícitamente", solo que no hay tenant que suministrar.
/// </param>
/// <param name="TemplateKey">
/// HU #11363 AC1 — id ESTABLE de la plantilla usada (mismo vocabulario que
/// <c>NotificationTemplateCatalog.TemplateIds</c> en <c>Flit.Infrastructure</c>, p. ej.
/// <c>"security.forgot-password"</c>). Se repite como literal escrito a mano en cada llamador
/// en vez de referenciar el catálogo — este proyecto (Domain) no puede depender de
/// Infrastructure, y el catálogo mismo declara sus ids como "literales estables" a propósito.
/// Lo consume el decorador de bitácora (<c>NotificationDeliveryLoggingEmailSender</c>); ninguna
/// implementación de transporte lo usa para enviar.
/// </param>
public sealed record EmailMessage(
    Guid? TenantId, string TemplateKey, string ToEmail, string ToName, string Subject, string HtmlBody);

/// <summary>
/// HU #11358 AC2 — catálogo CERRADO de causas de un intento de envío. <see cref="Sent"/> es la
/// única causa de éxito; las siete restantes son las ÚNICAS causas de fallo que
/// <see cref="IEmailSender"/> puede reportar. Ninguna implementación puede inventar una causa
/// fuera de este catálogo — mapea cualquier fallo no reconocido a la causa genérica más cercana
/// (normalmente <see cref="ProviderUnavailable"/>).
/// </summary>
public enum EmailSendOutcome
{
    /// <summary>Éxito: el proveedor aceptó el mensaje para entrega.</summary>
    Sent,

    /// <summary>El proveedor rechazó las credenciales de la cuenta remitente.</summary>
    AuthenticationFailed,

    /// <summary>El proveedor rechazó al destinatario (buzón inexistente, dominio inválido, etc.).</summary>
    RecipientRejected,

    /// <summary>El proveedor rechazó el contenido del mensaje (tamaño, formato, política de contenido).</summary>
    ContentRejected,

    /// <summary>Se alcanzó un límite de tasa/cuota del proveedor.</summary>
    RateLimited,

    /// <summary>El proveedor no está disponible (conexión rechazada, error de protocolo, 5xx transitorio).</summary>
    ProviderUnavailable,

    /// <summary>La operación agotó el tiempo de espera antes de obtener respuesta del proveedor.</summary>
    TimedOut,

    /// <summary>La configuración del transporte (host, credenciales, remitente) está incompleta.</summary>
    ConfigurationIncomplete,
}

/// <summary>
/// HU #11358 AC2/AC4 — resultado tipado de un intento de envío. <see cref="Message"/> es SIEMPRE
/// el texto genérico asociado a <see cref="Outcome"/> (ver <see cref="Failed"/>): nunca lleva
/// host, puerto, credencial ni el mensaje técnico crudo que devolvió el proveedor. Esa es la
/// frontera que impide que un fallo de autenticación SMTP, por ejemplo, filtre la contraseña de
/// la cuenta remitente hacia el llamador.
/// </summary>
public sealed record EmailSendResult(bool Success, EmailSendOutcome Outcome, string Message)
{
    /// <summary>
    /// HU #11364 AC2 — <c>true</c> cuando el ADAPTADOR DE CANAL (hoy, únicamente Renting fuera de
    /// producción; ver <c>RentingRecipientOverride</c>) sustituyó el destinatario real por uno de
    /// desvío ANTES de enviar. Propiedad no posicional, con default <c>false</c>, para que ningún
    /// llamador existente (SMTP, consola, los `Failed(outcome)`/`Sent` de abajo) tenga que cambiar:
    /// solo <c>RentingEmailApiSender</c> la enciende, con un <c>with</c> expression, sobre el
    /// resultado que ya construyó. El decorador de bitácora (HU #11363,
    /// <c>NotificationDeliveryLoggingEmailSender</c>) la lee para dejar la marca en
    /// <c>admin.notification_delivery_logs.recipient_diverted</c> — la fila ya lleva el destinatario
    /// REAL (lo toma de <see cref="EmailMessage"/>, antes de que el adaptador lo sustituya); sin
    /// esta marca, esa fila afirmaría (falsamente) que el correo llegó a ese destinatario.
    /// </summary>
    public bool RecipientDiverted { get; init; }

    /// <summary>Único resultado de éxito posible.</summary>
    public static readonly EmailSendResult Sent = new(true, EmailSendOutcome.Sent, "Correo enviado.");

    /// <summary>
    /// Construye un resultado de fallo con el mensaje genérico de <paramref name="outcome"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si <paramref name="outcome"/> es <see cref="EmailSendOutcome.Sent"/> — esa causa solo es
    /// válida a través de <see cref="Sent"/>.
    /// </exception>
    public static EmailSendResult Failed(EmailSendOutcome outcome)
    {
        if (!GenericMessages.TryGetValue(outcome, out var message))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome), outcome, "Sent no es una causa de fallo; usa EmailSendResult.Sent.");
        }

        return new EmailSendResult(false, outcome, message);
    }

    private static readonly Dictionary<EmailSendOutcome, string> GenericMessages =
        new Dictionary<EmailSendOutcome, string>
        {
            [EmailSendOutcome.AuthenticationFailed] = "No fue posible autenticar con el proveedor de correo.",
            [EmailSendOutcome.RecipientRejected] = "El proveedor de correo rechazó al destinatario.",
            [EmailSendOutcome.ContentRejected] = "El proveedor de correo rechazó el contenido del mensaje.",
            [EmailSendOutcome.RateLimited] = "Se alcanzó el límite de envíos del proveedor de correo.",
            [EmailSendOutcome.ProviderUnavailable] = "El proveedor de correo no está disponible en este momento.",
            [EmailSendOutcome.TimedOut] = "El envío del correo agotó el tiempo de espera.",
            [EmailSendOutcome.ConfigurationIncomplete] = "La configuración de correo está incompleta.",
        };
}

/// <summary>
/// Puerto de envío de correo. La implementación (SMTP/MailKit o consola en dev) vive
/// en la capa de infraestructura.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// HU #11358 AC2 — nunca lanza por un fallo de transporte (ver catálogo de
    /// <see cref="EmailSendOutcome"/>): esos fallos siempre vuelven como
    /// <see cref="EmailSendResult"/> con <c>Success = false</c>. Solo se propaga una excepción
    /// ante un fallo genuinamente inesperado (bug) o la cancelación del propio
    /// <paramref name="cancellationToken"/>.
    /// </summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
