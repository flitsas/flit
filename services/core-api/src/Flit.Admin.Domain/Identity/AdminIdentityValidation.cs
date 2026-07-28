namespace Flit.Admin.Domain.Identity;

/// <summary>
/// Validación de identidad ADMINISTRATIVA, DESACOPLADA de un trámite (HU #10907, ADR-0034). Bloque
/// AGNÓSTICO DEL SUJETO: se ancla a un sujeto por <see cref="SubjectType"/> +
/// <see cref="SubjectRef"/> (hoy <c>legal_representative</c>; reutilizable por el mandatario u otros
/// sin cambiar el agregado). Se inicia y reenvía por correo reutilizando el proveedor externo
/// (Kyverum), es VIGENTE por <see cref="AdminIdentityRules.VigenciaDias"/> días desde la aprobación y,
/// al aprobar, se VINCULA al sujeto (p.ej. <c>LegalRepresentative.LinkIdentity</c>).
/// <c>DocumentNumber</c>/<c>Email</c> son PII (Ley 1581): no loguear ni exponer en errores.
/// </summary>
public sealed class AdminIdentityValidation
{
    private AdminIdentityValidation(
        Guid id,
        Guid tenantId,
        string subjectType,
        Guid subjectRef,
        string documentType,
        string documentNumber,
        string name,
        string email,
        string status,
        string provider,
        string? captureUrl,
        string? kyverumVerificationId,
        string? webhookSecretEncrypted,
        string? providerStatus,
        string? providerPayload,
        string? certificateHash,
        DateTimeOffset? validatedAt,
        DateTimeOffset? validUntil,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        SubjectType = subjectType;
        SubjectRef = subjectRef;
        DocumentType = documentType;
        DocumentNumber = documentNumber;
        Name = name;
        Email = email;
        Status = status;
        Provider = provider;
        CaptureUrl = captureUrl;
        KyverumVerificationId = kyverumVerificationId;
        WebhookSecretEncrypted = webhookSecretEncrypted;
        ProviderStatus = providerStatus;
        ProviderPayload = providerPayload;
        CertificateHash = certificateHash;
        ValidatedAt = validatedAt;
        ValidUntil = validUntil;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    /// <summary>Tipo de sujeto anclado (p.ej. <c>legal_representative</c>). No acopla el bloque al sujeto.</summary>
    public string SubjectType { get; }

    /// <summary>Id del sujeto anclado en su tabla (p.ej. el id del representante legal).</summary>
    public Guid SubjectRef { get; }

    public string DocumentType { get; }

    /// <summary>Documento del sujeto. PII (@pii:high): no loguear.</summary>
    public string DocumentNumber { get; }

    /// <summary>Nombre del sujeto (se envía al proveedor). PII (@pii:medium): no loguear.</summary>
    public string Name { get; }

    /// <summary>Correo al que el proveedor notifica el enlace de captura. PII: no loguear.</summary>
    public string Email { get; }

    /// <summary>enviado | en_proceso | aprobado | rechazado | expirado.</summary>
    public string Status { get; private set; }

    /// <summary>Proveedor externo de identidad (<see cref="AdminIdentityProviders"/>).</summary>
    public string Provider { get; private set; }

    /// <summary>URL de captura que abre el sujeto para completar la validación.</summary>
    public string? CaptureUrl { get; private set; }

    /// <summary>Id de la verificación en el proveedor (correlación con la reconciliación).</summary>
    public string? KyverumVerificationId { get; private set; }

    /// <summary>Secreto HMAC del webhook CIFRADO. NUNCA se persiste en claro ni se expone.</summary>
    public string? WebhookSecretEncrypted { get; private set; }

    /// <summary>Estado crudo reportado por el proveedor. Trazabilidad.</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>Payload del proveedor SANITIZADO (sin PII cruda ni secretos). Trazabilidad.</summary>
    public string? ProviderPayload { get; private set; }

    /// <summary>Serie/hash del certificado de identidad emitido por el proveedor. Null hasta aprobar.</summary>
    public string? CertificateHash { get; private set; }

    /// <summary>Instante de aprobación. Null mientras no haya aprobación.</summary>
    public DateTimeOffset? ValidatedAt { get; private set; }

    /// <summary>
    /// Fin de vigencia de la identidad APROBADA (medianoche Colombia de
    /// <c>ValidatedAt + VigenciaDias</c>). Se ESTAMPA al aprobar. Null mientras no haya aprobación.
    /// </summary>
    public DateTimeOffset? ValidUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>¿Está APROBADA y VIGENTE en <paramref name="now"/>? Reuso de identidad (30 días).</summary>
    public bool EsAprobadaVigente(DateTimeOffset now) =>
        Status == AdminIdentityEstados.Aprobado && AdminIdentityRules.EsVigente(ValidUntil, ValidatedAt, now);

    /// <summary>
    /// Crea la validación en estado <c>enviado</c> tras el inicio exitoso con el proveedor (que notifica
    /// el enlace de captura por correo). Guarda la URL de captura, el id de verificación y el secreto del
    /// webhook YA CIFRADO. El sujeto se ancla por <paramref name="subjectType"/> + <paramref name="subjectRef"/>.
    /// </summary>
    public static AdminIdentityValidation CreateSent(
        Guid tenantId,
        string subjectType,
        Guid subjectRef,
        string documentType,
        string documentNumber,
        string name,
        string email,
        string provider,
        string? captureUrl,
        string? kyverumVerificationId,
        string? webhookSecretEncrypted,
        string? providerStatus,
        string? providerPayload,
        DateTimeOffset now,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("El tenant de la validación es obligatorio.", nameof(tenantId));
        }

        if (subjectRef == Guid.Empty)
        {
            throw new ArgumentException("El sujeto de la validación es obligatorio.", nameof(subjectRef));
        }

        return new AdminIdentityValidation(
            id ?? Guid.NewGuid(),
            tenantId,
            subjectType.Trim(),
            subjectRef,
            documentType.Trim(),
            documentNumber.Trim(),
            name.Trim(),
            email.Trim(),
            AdminIdentityEstados.Enviado,
            provider.Trim(),
            Normalize(captureUrl),
            Normalize(kyverumVerificationId),
            Normalize(webhookSecretEncrypted),
            Normalize(providerStatus),
            Normalize(providerPayload),
            certificateHash: null,
            validatedAt: null,
            validUntil: null,
            createdAt: now,
            updatedAt: null);
    }

    /// <summary>Rehidrata el agregado desde persistencia (sin aplicar invariantes de alta).</summary>
    public static AdminIdentityValidation Rehydrate(
        Guid id,
        Guid tenantId,
        string subjectType,
        Guid subjectRef,
        string documentType,
        string documentNumber,
        string name,
        string email,
        string status,
        string provider,
        string? captureUrl,
        string? kyverumVerificationId,
        string? webhookSecretEncrypted,
        string? providerStatus,
        string? providerPayload,
        string? certificateHash,
        DateTimeOffset? validatedAt,
        DateTimeOffset? validUntil,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt) =>
        new(id, tenantId, subjectType, subjectRef, documentType, documentNumber, name, email, status,
            provider, captureUrl, kyverumVerificationId, webhookSecretEncrypted, providerStatus,
            providerPayload, certificateHash, validatedAt, validUntil, createdAt, updatedAt);

    /// <summary>
    /// Marca APROBADA en <paramref name="now"/>: estado <c>aprobado</c> + <see cref="ValidatedAt"/> +
    /// ESTAMPA <see cref="ValidUntil"/> (<c>now + VigenciaDias</c>, medianoche Colombia) + el
    /// <paramref name="certificateHash"/> del proveedor. Idempotente: si ya está aprobada, no reinicia el
    /// reloj de vigencia (conserva el vencimiento original). Devuelve <c>true</c> si transicionó ahora.
    /// </summary>
    public bool Approve(DateTimeOffset now, string? certificateHash)
    {
        if (Status == AdminIdentityEstados.Aprobado)
        {
            // Idempotente: solo enriquece el certificado si aún no se había estampado.
            if (CertificateHash is null && !string.IsNullOrWhiteSpace(certificateHash))
            {
                CertificateHash = certificateHash.Trim();
                UpdatedAt = now;
            }

            return false;
        }

        Status = AdminIdentityEstados.Aprobado;
        ValidatedAt = now;
        ValidUntil = AdminIdentityRules.FechaFinVigencia(now);
        CertificateHash = Normalize(certificateHash);
        UpdatedAt = now;
        return true;
    }

    /// <summary>Marca RECHAZADA en <paramref name="now"/> (terminal). Idempotente.</summary>
    public void Reject(DateTimeOffset now, string? providerStatus = null)
    {
        if (Status == AdminIdentityEstados.Rechazado)
        {
            return;
        }

        Status = AdminIdentityEstados.Rechazado;
        ProviderStatus = Normalize(providerStatus) ?? ProviderStatus;
        UpdatedAt = now;
    }

    /// <summary>Marca EXPIRADA en <paramref name="now"/> (el enlace venció sin resolver). Idempotente.</summary>
    public void Expire(DateTimeOffset now)
    {
        if (Status is AdminIdentityEstados.Expirado or AdminIdentityEstados.Aprobado or AdminIdentityEstados.Rechazado)
        {
            return;
        }

        Status = AdminIdentityEstados.Expirado;
        UpdatedAt = now;
    }

    /// <summary>Registra el estado en curso reportado por el proveedor (trazabilidad, sin terminalizar).</summary>
    public void TrackProvider(string? providerStatus, string? providerPayload, DateTimeOffset now)
    {
        ProviderStatus = Normalize(providerStatus) ?? ProviderStatus;
        ProviderPayload = Normalize(providerPayload) ?? ProviderPayload;
        UpdatedAt = now;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Proveedores de validación de identidad administrativa (HU #10907).</summary>
public static class AdminIdentityProviders
{
    /// <summary>Kyverum Verify: captura remota + notificación por correo (reuso HU #10233).</summary>
    public const string Kyverum = "kyverum";
}

/// <summary>Tipos de sujeto anclables a una validación de identidad administrativa (agnóstico).</summary>
public static class AdminIdentitySubjectTypes
{
    /// <summary>Representante legal por compañía (HU #10900).</summary>
    public const string LegalRepresentative = "legal_representative";

    /// <summary>Mandatario (firmante de mandato) por OT (ADR-0036, HU #10911).</summary>
    public const string MandateSigner = "mandate_signer";
}

/// <summary>Estados de la máquina de la validación de identidad administrativa.</summary>
public static class AdminIdentityEstados
{
    public const string Enviado = "enviado";
    public const string EnProceso = "en_proceso";
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";
    public const string Expirado = "expirado";
}

/// <summary>
/// Reglas de vigencia de la identidad administrativa (HU #10907). Auto-contenidas para no acoplar el
/// módulo Admin a <c>Tramites.Domain</c>; el criterio (30 días, corte por día calendario Colombia) es el
/// mismo que la biométrica de trámite (HU #10350).
/// </summary>
public static class AdminIdentityRules
{
    /// <summary>
    /// Vigencia (días CALENDARIO) de una identidad APROBADA, contada desde la aprobación. El día de
    /// aprobación es el día 1; vence en el día 31 (<c>ValidatedAt + 30</c> ya NO es vigente).
    /// </summary>
    public const int VigenciaDias = 30;

    /// <summary>Huso horario de Colombia (UTC-5, sin horario de verano).</summary>
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    /// <summary>
    /// Fecha de fin de vigencia para una aprobación en <paramref name="validatedAt"/>: medianoche (hora
    /// Colombia) del día <c>validatedAt + VigenciaDias</c>, DEVUELTA en UTC (offset 0, requisito de Npgsql
    /// para <c>timestamptz</c>). Los lectores reconvierten a hora Colombia, así que el día no cambia.
    /// </summary>
    public static DateTimeOffset FechaFinVigencia(DateTimeOffset validatedAt)
    {
        var diaExpiracion = validatedAt.ToOffset(ColombiaUtcOffset).Date.AddDays(VigenciaDias);
        return new DateTimeOffset(diaExpiracion, ColombiaUtcOffset).ToUniversalTime();
    }

    /// <summary>
    /// ¿Una aprobación está VIGENTE en <paramref name="now"/>? <paramref name="validUntil"/> es la fuente
    /// de verdad cuando está estampada (editable en BD para vencer/extender); si falta, se cae al cálculo
    /// por <paramref name="validatedAt"/> + <see cref="VigenciaDias"/> (día calendario Colombia).
    /// </summary>
    public static bool EsVigente(DateTimeOffset? validUntil, DateTimeOffset? validatedAt, DateTimeOffset now)
    {
        if (validUntil is { } hasta)
        {
            return now < hasta;
        }

        if (validatedAt is not { } validado)
        {
            return true;
        }

        var hoy = now.ToOffset(ColombiaUtcOffset).Date;
        var diaAprobacion = validado.ToOffset(ColombiaUtcOffset).Date;
        return hoy < diaAprobacion.AddDays(VigenciaDias);
    }
}
