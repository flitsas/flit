namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Certificado (PDF) de una validación de identidad descargado desde el panel admin de Kyverum.
/// </summary>
/// <param name="Content">Bytes del documento (normalmente PDF).</param>
/// <param name="ContentType">Content-Type reportado por Kyverum (default <c>application/pdf</c>).</param>
/// <param name="FileName">Nombre sugerido del archivo (de <c>Content-Disposition</c> o uno por defecto).</param>
public sealed record KyverumCertificate(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Error al descargar el certificado desde Kyverum. <see cref="Transient"/> distingue fallos transitorios
/// (timeout / 5xx → reintentar) de definitivos (4xx / login inválido). El mensaje NUNCA contiene el
/// password de la cuenta admin ni la cookie de sesión.
/// </summary>
public sealed class KyverumCertificateException(string message, bool transient) : Exception(message)
{
    public bool Transient { get; } = transient;
}

/// <summary>
/// Contrato del cliente que descarga el certificado (PDF) de una validación desde la API pública de
/// Kyverum (<c>GET /v1/validations/{id}/certificado</c>), con el mismo Bearer API key que el create. La
/// implementación vive en Infraestructura (<c>KyverumCertificateClient</c>, typed HttpClient) — mismo
/// patrón contract-first que <see cref="IKyverumVerifyClient"/>.
/// </summary>
public interface IKyverumCertificateClient
{
    /// <summary>
    /// Descarga el certificado de la validación <paramref name="verificationId"/> (id de Kyverum). Devuelve
    /// <c>null</c> si Kyverum no tiene certificado para ese id (404). Lanza <see cref="KyverumCertificateException"/>
    /// ante 4xx (definitivo) o 5xx/timeout/red (transitorio), sin filtrar secretos.
    /// </summary>
    Task<KyverumCertificate?> DownloadCertificateAsync(string verificationId, CancellationToken ct = default);
}
