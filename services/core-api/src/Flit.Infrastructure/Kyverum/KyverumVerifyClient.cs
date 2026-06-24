using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Tramites.Application.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Kyverum;

/// <summary>
/// Cliente HTTP de Kyverum Verify (HU #10233). Crea una validación remota (<c>POST /v1/validations</c>)
/// y devuelve la URL de captura. El secreto con el que Kyverum firma el webhook es por-tenant (dashboard),
/// se toma de <see cref="KyverumOptions.WebhookSecret"/> y el handler lo persiste cifrado. Errores se
/// mapean a <see cref="KyverumVerifyException"/> SIN incluir nunca la API key ni el secreto (AC7):
/// 4xx ⇒ definitivo (502); 5xx/timeout/red/respuesta inválida ⇒ transitorio (503).
/// </summary>
internal sealed class KyverumVerifyClient(
    HttpClient http,
    IOptions<KyverumOptions> options,
    ILogger<KyverumVerifyClient> logger) : IKyverumVerifyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly KyverumOptions _options = options.Value;

    public async Task<KyverumVerifyStartResult> StartVerificationAsync(KyverumVerifyStartRequest request, CancellationToken ct)
    {
        var body = new KyverumCreateValidationBody(
            ExternalRef: request.ProcedureInstanceId.ToString("D"),
            Metadata: new KyverumMetadata(request.Parte),
            // El webhook de Kyverum no repite nuestro id en el cuerpo: lo incrustamos en la URL de callback
            // para poder correlacionar la notificación con la validación.
            WebhookUrl: BuildWebhookUrl(request.CorrelationId),
            Subjects:
            [
                new KyverumSubject(
                    Rol: string.IsNullOrWhiteSpace(request.Parte) ? "titular" : request.Parte!,
                    Nombre: request.Nombre,
                    TipoDoc: request.TipoDoc,
                    Documento: request.Documento,
                    // Kyverum notifica al sujeto el enlace de captura usando este correo (subjects[].email).
                    Email: string.IsNullOrWhiteSpace(request.Email) ? null : request.Email),
            ]);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/validations")
            {
                Content = JsonContent.Create(body, options: JsonOptions),
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                message.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Idempotencia del create en Kyverum (evita validaciones duplicadas ante reintentos).
            message.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));

            using var response = await http.SendAsync(message, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Loguea el cuerpo de error de Kyverum (no contiene nuestra API key) para diagnóstico.
                var errorBody = await SafeReadBodyAsync(response, ct);
                var transient = (int)response.StatusCode >= 500;
                KyverumLog.ProviderError(logger, (int)response.StatusCode, errorBody);
                throw new KyverumVerifyException(
                    transient
                        ? $"Kyverum no disponible ({(int)response.StatusCode})."
                        : $"Kyverum rechazó la solicitud ({(int)response.StatusCode}).",
                    transient);
            }

            var payload = await response.Content.ReadFromJsonAsync<KyverumCreateValidationResponse>(JsonOptions, ct);
            var captureUrl = payload?.CaptureLinks is { Count: > 0 } links ? links[0].CaptureUrl : null;
            if (payload is null || string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(captureUrl))
                throw new KyverumVerifyException("Respuesta inválida de Kyverum.", transient: false);

            var status = string.IsNullOrWhiteSpace(payload.Status) ? "pending" : payload.Status!;
            var sanitized = JsonSerializer.Serialize(new
            {
                id = payload.Id,
                status,
                proveedor = "kyverum",
            });

            // El secreto del webhook viene en la respuesta del create (firma los callbacks). Puede ser
            // vacío si el plan/tenant no lo expone; en ese caso el webhook fallará cerrado (401).
            return new KyverumVerifyStartResult(payload.Id!, captureUrl!, payload.WebhookSecret ?? string.Empty, status, sanitized);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new KyverumVerifyException("Timeout consultando Kyverum.", transient: true);
        }
        catch (HttpRequestException ex)
        {
            throw new KyverumVerifyException($"Error de red consultando Kyverum: {ex.Message}", transient: true);
        }
        catch (JsonException)
        {
            throw new KyverumVerifyException("No se pudo interpretar la respuesta de Kyverum.", transient: false);
        }
    }

    /// <summary>
    /// URL de callback registrada en Kyverum, con nuestro id de correlación incrustado en el path.
    /// Devuelve null si no hay base configurada (Kyverum exige una URL pública; sin ella el create falla).
    /// </summary>
    private string? BuildWebhookUrl(Guid correlationId)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookCallbackUrl))
            return null;
        return $"{_options.WebhookCallbackUrl.TrimEnd('/')}/{correlationId:D}";
    }

    /// <summary>Lee el cuerpo de error del proveedor (truncado) sin lanzar. Solo para diagnóstico.</summary>
    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return body.Length > 500 ? body[..500] : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return "(sin cuerpo)";
        }
    }

    // ── Contrato Kyverum (colección Postman "Kyverum Verify") ─────────────────
    private sealed record KyverumCreateValidationBody(
        [property: JsonPropertyName("externalRef")] string ExternalRef,
        [property: JsonPropertyName("metadata")] KyverumMetadata Metadata,
        [property: JsonPropertyName("webhookUrl")] string? WebhookUrl,
        [property: JsonPropertyName("subjects")] IReadOnlyList<KyverumSubject> Subjects);

    private sealed record KyverumMetadata(
        [property: JsonPropertyName("parte")] string? Parte);

    private sealed record KyverumSubject(
        [property: JsonPropertyName("rol")] string Rol,
        [property: JsonPropertyName("nombre")] string Nombre,
        [property: JsonPropertyName("tipoDoc")] string TipoDoc,
        [property: JsonPropertyName("documento")] string Documento,
        // Se omite del JSON si es null (Kyverum lo trata como opcional); presente ⇒ Kyverum notifica al sujeto.
        [property: JsonPropertyName("email"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Email);

    private sealed record KyverumCreateValidationResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("webhookSecret")] string? WebhookSecret,
        [property: JsonPropertyName("captureLinks")] IReadOnlyList<KyverumCaptureLink>? CaptureLinks);

    private sealed record KyverumCaptureLink(
        [property: JsonPropertyName("captureUrl")] string? CaptureUrl);
}

/// <summary>Logging source-generated (CA1848) del cliente Kyverum.</summary>
internal static partial class KyverumLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Kyverum respondió error HTTP {StatusCode}. Cuerpo: {ErrorBody}")]
    public static partial void ProviderError(ILogger logger, int statusCode, string errorBody);
}
