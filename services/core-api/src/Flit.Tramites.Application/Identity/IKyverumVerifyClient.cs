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
public sealed record KyverumVerifyStartRequest(
    Guid ProcedureInstanceId,
    Guid CorrelationId,
    string? Parte,
    string Nombre,
    string TipoDoc,
    string Documento);

/// <summary>
/// Resultado de iniciar una verificación en Kyverum. El <paramref name="WebhookSecret"/> es el secreto
/// HMAC con el que Kyverum firmará el webhook de esta verificación: viaja en CLARO solo en memoria y el
/// handler lo cifra (Data Protection) antes de persistirlo. <paramref name="RawPayloadSanitized"/> es el
/// cuerpo del proveedor ya SANITIZADO (sin secretos ni PII cruda) para trazabilidad.
/// </summary>
public sealed record KyverumVerifyStartResult(
    string VerificationId,
    string CaptureUrl,
    string WebhookSecret,
    string ProviderStatus,
    string RawPayloadSanitized);

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
/// Contrato del cliente HTTP de Kyverum Verify (HU #10233). La implementación vive en Infraestructura
/// (<c>KyverumVerifyClient</c>, typed HttpClient) — mismo patrón contract-first que los consultation
/// providers. Lanza <see cref="KyverumVerifyException"/> ante 4xx/5xx/timeout (el handler los mapea a
/// 502/503 sin filtrar secretos).
/// </summary>
public interface IKyverumVerifyClient
{
    Task<KyverumVerifyStartResult> StartVerificationAsync(KyverumVerifyStartRequest request, CancellationToken ct = default);
}
