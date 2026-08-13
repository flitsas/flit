namespace Flit.Admin.Application.Identity;

/// <summary>
/// Datos para iniciar una validación de identidad administrativa (provider-agnostic, ADR-0034). El
/// <see cref="ValidationId"/> es el id de NUESTRA validación (se incrusta en la callback URL del
/// proveedor para correlacionar la reconciliación). <see cref="SubjectRef"/> es el id del sujeto anclado
/// (p.ej. el representante legal): viaja al proveedor como referencia externa OPACA, desacoplada de
/// cualquier trámite — el proveedor no exige que exista un trámite (riesgo R2 de ADR-0034 acotado).
/// </summary>
public sealed record AdminIdentityStartRequest(
    Guid TenantId,
    Guid ValidationId,
    string SubjectType,
    Guid SubjectRef,
    string Name,
    string DocumentType,
    string DocumentNumber,
    string Email);

/// <summary>
/// Resultado del inicio con el proveedor. <see cref="WebhookSecretEncrypted"/> viene YA CIFRADO por el
/// adaptador (Data Protection): la capa de aplicación NUNCA maneja el secreto en claro. Forma común a
/// todos los proveedores.
/// </summary>
public sealed record AdminIdentityStartResult(
    string? VerificationId,
    string? CaptureUrl,
    string? WebhookSecretEncrypted,
    string? ProviderStatus,
    string? RawPayloadSanitized);

/// <summary>
/// Estado consultado al proveedor durante la reconciliación (ADR-0034). <see cref="Approved"/>,
/// <see cref="Rejected"/> y <see cref="RejectedAttempt"/> son mutuamente excluyentes; los tres
/// <c>false</c> = sigue en proceso. <see cref="CertificateHash"/> es la serie/hash del certificado de
/// identidad (solo si aprobó).
///
/// <para><b><see cref="Rejected"/></b> — el proveedor CERRÓ la validación rechazada (terminal,
/// autoritativo): se aplica de inmediato aunque el conteo local de intentos aún tenga margen.
/// <b><see cref="RejectedAttempt"/></b> — un INTENTO falló SIN señal de cierre (HU #11504, la
/// contraparte admin del Bug #11503): no es terminal por sí solo. El reconciliador cuenta los intentos
/// (dedup por <see cref="AttemptKey"/>, ver <c>AdminIdentityValidation.RegisterFailedAttempt</c>) y solo
/// terminaliza al alcanzar el máximo. <see cref="AttemptKey"/> es la clave de dedup de ESTE intento
/// (p. ej. el <c>AttemptAt</c>/<c>validadoAt</c> de Kyverum): null si el proveedor no la reportó.</para>
/// </summary>
public sealed record AdminIdentityStatusResult(
    bool Approved,
    bool Rejected,
    string? ProviderStatus,
    string? CertificateHash,
    string? RawPayloadSanitized,
    bool RejectedAttempt = false,
    string? AttemptKey = null);

/// <summary>
/// Error al operar con el proveedor. <see cref="Transient"/> = reintentable (proveedor caído, timeout,
/// 5xx); <c>false</c> = definitivo (datos inválidos, 4xx). El mensaje NUNCA contiene la API key ni el
/// secreto del webhook.
/// </summary>
public sealed class AdminIdentityProviderException(string message, bool transient) : Exception(message)
{
    public bool Transient { get; } = transient;
}

/// <summary>
/// Puerto (hexagonal) del proveedor de validación de identidad administrativa DESACOPLADA de un trámite
/// (HU #10907, ADR-0034). El adaptador vive en Infrastructure y REUTILIZA el cliente Kyverum de forma
/// desacoplada (referencia externa opaca + correlación por URL de callback). Modelarlo como puerto deja
/// el flujo compilable y testeable con un proveedor fake.
/// </summary>
public interface IAdminIdentityValidationProvider
{
    /// <summary>Nombre del proveedor (<see cref="Flit.Admin.Domain.Identity.AdminIdentityProviders"/>).</summary>
    string Name { get; }

    /// <summary>
    /// Inicia la validación remota (el proveedor notifica el enlace de captura por correo). Lanza
    /// <see cref="AdminIdentityProviderException"/> (transitorio/definitivo) si el proveedor falla.
    /// </summary>
    Task<AdminIdentityStartResult> StartAsync(AdminIdentityStartRequest request, CancellationToken ct = default);

    /// <summary>
    /// Consulta el estado actual de una verificación (reconciliación). <c>null</c> si el proveedor no
    /// conoce el id. Lanza <see cref="AdminIdentityProviderException"/> ante fallos del proveedor.
    /// </summary>
    Task<AdminIdentityStatusResult?> GetStatusAsync(string verificationId, CancellationToken ct = default);
}
