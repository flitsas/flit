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
}
