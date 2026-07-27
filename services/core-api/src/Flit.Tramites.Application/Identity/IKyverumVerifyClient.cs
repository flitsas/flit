namespace Flit.Tramites.Application.Identity;

/// <summary>Datos para iniciar una verificación de identidad en Kyverum (sin PII innecesaria).</summary>
/// <param name="ProcedureInstanceId">Instancia de trámite (se envía como externalRef).</param>
/// <param name="CorrelationId">
/// Id de NUESTRA validación. Se incrusta en la <c>webhookUrl</c> registrada porque el webhook de Kyverum
/// NO repite el id de la validación en el cuerpo — la correlación se hace por la URL del callback.
/// </param>
/// <param name="Parte">comprador|vendedor|null — parte del trámite.</param>
/// <param name="Nombre">Nombre de la persona a validar.</param>
/// <param name="TipoDoc">Tipo de documento (CC, CE, …).</param>
/// <param name="Documento">Número de documento.</param>
/// <param name="Email">Correo de la persona: Kyverum lo usa para notificarle el enlace de captura.</param>
public sealed record KyverumVerifyStartRequest(
    /// <summary>HU #10865 — nullable para prevalidaciones standalone (sin trámite).</summary>
    Guid? ProcedureInstanceId,
    Guid CorrelationId,
    string? Parte,
    string Nombre,
    string TipoDoc,
    string Documento,
    string Email);

/// <summary>
/// Resultado de iniciar una verificación en Kyverum. El <paramref name="WebhookSecret"/> es el secreto
/// HMAC con el que Kyverum firmará el webhook de esta verificación: viaja en CLARO solo en memoria y el
/// handler lo cifra (Data Protection) antes de persistirlo. <paramref name="RawPayloadSanitized"/> es el
/// cuerpo del proveedor ya SANITIZADO (sin secretos ni PII cruda) para trazabilidad.
/// <paramref name="ExpiresAt"/> es el vencimiento REAL del enlace de captura reportado por Kyverum
/// (<c>expiresAt</c>): es null si el proveedor no lo envía y el handler cae al TTL local por defecto.
/// </summary>
public sealed record KyverumVerifyStartResult(
    string VerificationId,
    string CaptureUrl,
    string WebhookSecret,
    string ProviderStatus,
    string RawPayloadSanitized,
    DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Error del proveedor Kyverum al iniciar una verificación. <see cref="Transient"/> distingue fallos
/// transitorios (timeout / 5xx → 503) de los definitivos (4xx → 502). El mensaje NUNCA contiene la
/// API key ni el secreto del webhook (AC7: no filtrar secretos).
/// </summary>
public sealed class KyverumVerifyException(string message, bool transient) : Exception(message)
{
    public bool Transient { get; } = transient;
}

/// <summary>
/// Estado de una validación consultado a Kyverum (<c>GET /v1/validations/{id}</c>), usado por la
/// reconciliación. <paramref name="Status"/> ya viene NORMALIZADO por el cliente al veredicto FLIT:
/// <list type="bullet">
///   <item><c>aprobado</c> — el <c>result</c> de Kyverum cerró aprobado.</item>
///   <item><c>rechazado_intento</c> — un INTENTO falló (Kyverum reporta result/rechazado tras CADA intento,
///   aunque queden reintentos). NO es terminal por sí solo: el reconciliador CUENTA los intentos (dedup por
///   <paramref name="AttemptAt"/>) y solo terminaliza en <c>rechazado</c> al llegar al máximo.</item>
///   <item><c>en_proceso</c>/<c>enviado</c>/<c>expirado</c> — tal cual.</item>
/// </list>
/// <paramref name="AttemptAt"/> = <c>validadoAt</c> del último intento (clave de dedup/conteo);
/// <paramref name="Motivo"/> = mensaje amigable de Kyverum del intento; <paramref name="RawPayloadSanitized"/>
/// = payload SANITIZADO (sin OCR/PII) para trazabilidad.
/// </summary>
public sealed record KyverumVerifyStatus(
    string Status, int? Score, string RawPayloadSanitized, string? AttemptAt = null, string? Motivo = null,
    string? FirmaSerie = null);

/// <summary>
/// Contrato del cliente HTTP de Kyverum Verify (HU #10233). La implementación vive en Infraestructura
/// (<c>KyverumVerifyClient</c>, typed HttpClient) — mismo patrón contract-first que los consultation
/// providers. Lanza <see cref="KyverumVerifyException"/> ante 4xx/5xx/timeout (el handler los mapea a
/// 502/503 sin filtrar secretos).
/// </summary>
public interface IKyverumVerifyClient
{
    Task<KyverumVerifyStartResult> StartVerificationAsync(KyverumVerifyStartRequest request, CancellationToken ct = default);

    /// <summary>
    /// Consulta el estado actual de una validación en Kyverum (<c>GET /v1/validations/{id}</c>). Reconciliación
    /// cuando el webhook se pierde. Devuelve <c>null</c> si Kyverum no conoce el id (404). <paramref name="parte"/>
    /// selecciona el subject correspondiente (o el primero). Lanza <see cref="KyverumVerifyException"/> ante
    /// 5xx/timeout/red (transitorio) o respuesta inválida (definitivo).
    /// </summary>
    Task<KyverumVerifyStatus?> GetStatusAsync(string verificationId, string? parte, CancellationToken ct = default);
}
