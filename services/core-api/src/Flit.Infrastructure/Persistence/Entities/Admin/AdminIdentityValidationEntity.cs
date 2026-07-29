namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Validación de identidad administrativa desacoplada — <c>admin.admin_identity_validations</c>
/// (HU #10907, ADR-0034). Bloque AGNÓSTICO del sujeto: anclada por (<c>SubjectType</c>,
/// <c>SubjectRef</c>), tenant-scoped (RLS por <c>tenant_id</c>). <c>DocumentNumber</c>/<c>Email</c> son
/// PII (Ley 1581): no loguear. <c>WebhookSecretEncrypted</c> se guarda CIFRADO, nunca en claro.
/// </summary>
public sealed class AdminIdentityValidationEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string SubjectType { get; set; } = string.Empty;

    public Guid SubjectRef { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Documento del sujeto. PII (@pii:high): no loguear.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>Nombre del sujeto. PII (@pii:medium): no loguear.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Correo del sujeto. PII: no loguear.</summary>
    public string Email { get; set; } = string.Empty;

    public string Status { get; set; } = "enviado";

    public string Provider { get; set; } = "kyverum";

    public string? CaptureUrl { get; set; }

    public string? KyverumVerificationId { get; set; }

    /// <summary>Secreto HMAC del webhook CIFRADO (Data Protection). Nunca en claro.</summary>
    public string? WebhookSecretEncrypted { get; set; }

    public string? ProviderStatus { get; set; }

    public string? ProviderPayload { get; set; }

    public string? CertificateHash { get; set; }

    public DateTimeOffset? ValidatedAt { get; set; }

    public DateTimeOffset? ValidUntil { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
