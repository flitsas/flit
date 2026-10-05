namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Bitácora ÚNICA y consultable del ciclo de vida de una validación de identidad (Kyverum): envío al
/// proveedor, respuesta del create, llegada del webhook, si había secreto, si se pudo descifrar (o por qué
/// no), verificación de firma, resultado aplicado, y reconciliación por consulta. Una fila por PASO. Pensada
/// para diagnosticar "qué pasó" con un simple <c>SELECT ... WHERE validation_id = X ORDER BY occurred_at</c>,
/// sin ir a los logs del pod ni cruzar tablas. SANITIZADA: nunca guarda el secreto ni PII cruda (OCR).
/// </summary>
public sealed class IdentityValidationAuditEvent
{
    public Guid Id { get; set; }

    /// <summary>Momento del paso.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Paso del ciclo — ver <see cref="IdentityValidationAuditStages"/>.</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>Desenlace del paso (ok|error|approved|rejected|pending|not_found|decrypt_failed|…).</summary>
    public string Outcome { get; set; } = string.Empty;

    public Guid? TenantId { get; set; }
    public Guid? ProcedureInstanceId { get; set; }

    /// <summary>NUESTRO id de validación (procedure_instance_biometric_validations.id).</summary>
    public Guid? ValidationId { get; set; }

    /// <summary>Id de la verificación en Kyverum.</summary>
    public string? KyverumVerificationId { get; set; }

    public string? PartyRole { get; set; }

    /// <summary>Status HTTP que devolvimos (webhook) o recibimos del proveedor.</summary>
    public int? HttpStatus { get; set; }

    /// <summary>¿El webhook trajo header de firma?</summary>
    public bool? SignaturePresent { get; set; }

    /// <summary>¿La validación tenía secreto de webhook guardado?</summary>
    public bool? SecretPresent { get; set; }

    /// <summary>¿Se pudo DESCIFRAR el secreto? (false = keyring/appname no coincide → no se pudo verificar firma).</summary>
    public bool? DecryptOk { get; set; }

    /// <summary>Estado crudo del proveedor en este paso (enviado|aprobado|rechazado|validation.completed…).</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>Tipo de excepción si hubo error (p.ej. CryptographicException).</summary>
    public string? ErrorType { get; set; }

    /// <summary>Mensaje legible del paso (sin secretos ni PII).</summary>
    public string? Message { get; set; }

    /// <summary>Detalle SANITIZADO en jsonb (ids/estados/score), opcional.</summary>
    public string? Detail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Pasos del ciclo de vida auditados (<see cref="IdentityValidationAuditEvent.Stage"/>).</summary>
public static class IdentityValidationAuditStages
{
    /// <summary>Envío al proveedor (create de la validación).</summary>
    public const string Send = "send";

    /// <summary>Respuesta del create del proveedor.</summary>
    public const string SendResponse = "send_response";

    /// <summary>El create del proveedor falló.</summary>
    public const string SendError = "send_error";

    /// <summary>Llegó un webhook del proveedor.</summary>
    public const string WebhookReceived = "webhook_received";

    /// <summary>No se pudo verificar la firma (secreto ausente o indescifrable).</summary>
    public const string WebhookNotVerifiable = "webhook_not_verifiable";

    /// <summary>La firma del webhook no es válida.</summary>
    public const string WebhookSignatureInvalid = "webhook_signature_invalid";

    /// <summary>Se aplicó el resultado del webhook.</summary>
    public const string WebhookApplied = "webhook_applied";

    /// <summary>Reconciliación por consulta al proveedor (endpoint o worker).</summary>
    public const string Reconcile = "reconcile";

    /// <summary>El enlace de captura venció (expires_at &lt;= now) y la validación se terminalizó como expirada.</summary>
    public const string Expired = "expired";

    /// <summary>
    /// HU #10943 (CF-03) — se editaron los datos de contacto de una prevalidación standalone (nombre y/o
    /// correo del sujeto). El <c>Detail</c>/<c>Message</c> NUNCA lleva el correo en claro (enmascarado).
    /// </summary>
    public const string ContactEdited = "contact_edited";

    /// <summary>HU #10943 (CF-03) — se reenvió la validación (manual o automático por cambio de correo).</summary>
    public const string Resend = "resend";

    /// <summary>
    /// HU #13284 (Épica #13202) — el Super Admin activó el flujo manual sobre la validación. Sin PII, sin secretos y
    /// sin el token del enlace: solo ids, estado y proveedor previos y la vigencia.
    /// </summary>
    public const string ManualActivado = "manual_activado";

    /// <summary>
    /// HU #13289 (Feature #13281 B) — la persona aceptó el consentimiento biométrico en la captura manual. Sin PII ni
    /// IP en el mensaje: la IP vive solo en la columna <c>consent_ip</c> de la validación.
    /// </summary>
    public const string ManualConsentimiento = "manual_consentimiento";

    /// <summary>
    /// HU #13290 (Feature #13281 B) — llegó la captura manual (rostro, anverso, reverso y firma) y la validación pasó a
    /// revisión. Sin PII ni contenido de imágenes: SHA-256 de cada una, rutas opacas de storage (incluidas las del intento
    /// previo, que se conservan) y agente de usuario.
    /// </summary>
    public const string ManualCapturaRecibida = "manual_captura_recibida";

    /// <summary>
    /// HU #13284 — la activación del flujo manual canceló una verificación Kyverum en curso (enviada, en proceso,
    /// encolada o con error de envío). <c>KyverumVerificationId</c> del evento conserva el id externo solo como
    /// trazabilidad; en la fila de la validación ya no existe.
    /// </summary>
    public const string KyverumCanceladoPorManual = "kyverum_cancelado_por_manual";

    /// <summary>
    /// HU #13286 — llegó un webhook de Kyverum para una validación que ya es del flujo manual: se responde 200
    /// (para que Kyverum no reintente) sin aplicar nada. Sin payload, sin firma y sin secretos en el evento.
    /// </summary>
    public const string WebhookIgnoradoManual = "webhook_ignorado_manual";

    /// <summary>
    /// HU #13287 — el Super Admin regeneró el enlace de captura manual: el token anterior dejó de valer. Sin PII ni token.
    /// </summary>
    public const string ManualEnlaceRegenerado = "manual_enlace_regenerado";

    /// <summary>
    /// HU #13287 — el correo con el enlace de captura manual NO salió (titular sin correo o fallo de envío). La activación o la
    /// regeneración ya estaban confirmadas; el Super Admin puede regenerar. Sin correo, sin token y sin PII.
    /// </summary>
    public const string ManualCorreoFallido = "manual_correo_fallido";

    /// <summary>
    /// HU #13297 (Feature #13282 C) — el Super Admin consultó el detalle de una validación manual o una de sus imágenes
    /// (rostro, documento, firma). Sin PII ni rutas de storage: solo el usuario y qué se consultó (<c>recurso=detalle</c> o
    /// <c>recurso=imagen:&lt;kind&gt;</c>).
    /// </summary>
    public const string ManualImagenesConsultadas = "manual_imagenes_consultadas";
}

/// <summary>Desenlaces comunes (<see cref="IdentityValidationAuditEvent.Outcome"/>).</summary>
public static class IdentityValidationAuditOutcomes
{
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Received = "received";
    public const string NotFound = "not_found";
    public const string Approved = "aprobado";
    public const string Rejected = "rechazado";
    public const string Pending = "pendiente";
    public const string SecretMissing = "secret_missing";
    public const string DecryptFailed = "decrypt_failed";
    public const string SignatureInvalid = "firma_invalida";
    public const string ProviderUnavailable = "proveedor_no_disponible";
    public const string Expired = "expirado";

    /// <summary>Bug #13055 — el trámite dueño está anulado o revocado: el resultado se ignora.</summary>
    public const string TramiteInactivo = "tramite_inactivo";
}
