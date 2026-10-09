using System.Net;
using System.Net.Http.Headers;
using Flit.Tramites.Application.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Kyverum;

/// <summary>
/// Descarga el certificado (PDF) de una validación desde la API pública de Kyverum
/// (<c>GET /v1/validations/{id}/certificado</c>). Usa el MISMO Bearer API key que el create
/// (<see cref="KyverumVerifyClient"/>) — no requiere cookie ni login admin. Errores se mapean a
/// <see cref="KyverumCertificateException"/> sin filtrar la API key: 404 ⇒ null (sin certificado);
/// otros 4xx ⇒ definitivo; 5xx/timeout/red ⇒ transitorio.
/// </summary>
internal sealed class KyverumCertificateClient(
    HttpClient http,
    IOptions<KyverumOptions> options,
    ILogger<KyverumCertificateClient> logger) : IKyverumCertificateClient
{
    private readonly KyverumOptions _options = options.Value;

    public async Task<KyverumCertificate?> DownloadCertificateAsync(string verificationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(verificationId))
            throw new KyverumCertificateException("Falta el id de la validación de Kyverum.", transient: false);

        try
        {
            var path = $"/v1/validations/{Uri.EscapeDataString(verificationId)}/certificado";
            using var message = new HttpRequestMessage(HttpMethod.Get, path);
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                message.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));

            using var response = await http.SendAsync(message, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null; // Kyverum no tiene certificado para ese id.

            if (!response.IsSuccessStatusCode)
            {
                var transient = (int)response.StatusCode >= 500;
                KyverumCertificateLog.DownloadError(logger, (int)response.StatusCode);
                throw new KyverumCertificateException(
                    transient
                        ? $"Kyverum no disponible al descargar el certificado ({(int)response.StatusCode})."
                        : $"Kyverum rechazó la descarga del certificado ({(int)response.StatusCode}).",
                    transient);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
                throw new KyverumCertificateException("Kyverum devolvió un certificado vacío.", transient: false);

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
            var fileName = ResolveFileName(response, verificationId);
            return new KyverumCertificate(bytes, contentType, fileName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new KyverumCertificateException("Timeout consultando el certificado de Kyverum.", transient: true);
        }
        catch (HttpRequestException ex)
        {
            throw new KyverumCertificateException($"Error de red consultando el certificado de Kyverum: {ex.Message}", transient: true);
        }
    }

    private static string ResolveFileName(HttpResponseMessage response, string verificationId)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        return string.IsNullOrWhiteSpace(name)
            ? $"certificado_identidad_{verificationId}.pdf"
            : name.Trim('"');
    }
}

/// <summary>Logging source-generated (CA1848) del cliente de certificado. NUNCA incluye la API key.</summary>
internal static partial class KyverumCertificateLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Descarga de certificado Kyverum respondió HTTP {StatusCode}.")]
    public static partial void DownloadError(ILogger logger, int statusCode);
}
