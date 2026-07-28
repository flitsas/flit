namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Datos para iniciar una validación de identidad con un proveedor externo (provider-agnostic). El
/// <c>ValidationId</c> es el id de NUESTRA validación (se incrusta en la callback URL del proveedor).
/// </summary>
public sealed record IdentityProviderStartRequest(
    /// <summary>HU #10865 — nullable para prevalidaciones standalone (sin trámite).</summary>
    Guid? ProcedureInstanceId,
    Guid ValidationId,
    string? Parte,
    string Nombre,
    string TipoDoc,
    string Documento,
    string Email);

/// <summary>
/// Resultado del inicio con el proveedor: id de verificación remoto, URL de captura, secreto del webhook
/// (en claro; el caller lo cifra antes de persistir) y trazabilidad sanitizada. Forma común a todos los
/// proveedores; cada implementación mapea su respuesta a esta.
/// </summary>
public sealed record IdentityProviderStartResult(
    string? VerificationId,
    string CaptureUrl,
    string? WebhookSecret,
    string? ProviderStatus,
    string? RawPayloadSanitized);

/// <summary>
/// Error al iniciar con el proveedor. <see cref="Transient"/> = el fallo es reintentable (proveedor caído,
/// timeout, 5xx) → se ENCOLA y reintenta; <c>false</c> = definitivo (datos inválidos, 4xx) → no se reintenta.
/// </summary>
public sealed class IdentityProviderException(string message, bool transient) : Exception(message)
{
    public bool Transient { get; } = transient;
}

/// <summary>
/// Proveedor de validación de identidad (Kyverum, o cualquier otro). Abstrae el inicio de la captura para
/// que la COLA DE ENVÍO y su reintento sean independientes del proveedor: cambiar/añadir proveedor =
/// implementar esta interfaz; el worker de reintento no se toca.
/// </summary>
public interface IIdentityValidationProvider
{
    /// <summary>Nombre del proveedor (<see cref="Flit.Tramites.Domain.Entities.BiometricProviders"/>).</summary>
    string Name { get; }

    /// <summary>
    /// Inicia la validación remota. Lanza <see cref="IdentityProviderException"/> (transitorio/definitivo)
    /// si el proveedor falla.
    /// </summary>
    Task<IdentityProviderStartResult> StartAsync(IdentityProviderStartRequest request, CancellationToken ct = default);
}

/// <summary>Resuelve el <see cref="IIdentityValidationProvider"/> por nombre (provider-agnostic).</summary>
public interface IIdentityValidationProviderResolver
{
    /// <summary>Devuelve el proveedor con ese nombre, o null si no hay ninguno registrado para él.</summary>
    IIdentityValidationProvider? Resolve(string providerName);
}
