using System.Net;
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

    public async Task<KyverumVerifyStatus?> GetStatusAsync(string verificationId, string? parte, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(verificationId))
            throw new KyverumVerifyException("Falta el id de la validación de Kyverum.", transient: false);

        try
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Get, $"/v1/validations/{Uri.EscapeDataString(verificationId)}");
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                message.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(message, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null; // Kyverum no conoce ese id.

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await SafeReadBodyAsync(response, ct);
                var transient = (int)response.StatusCode >= 500;
                KyverumLog.ProviderError(logger, (int)response.StatusCode, errorBody);
                throw new KyverumVerifyException(
                    transient
                        ? $"Kyverum no disponible al consultar el estado ({(int)response.StatusCode})."
                        : $"Kyverum rechazó la consulta de estado ({(int)response.StatusCode}).",
                    transient);
            }

            var payload = await response.Content.ReadFromJsonAsync<KyverumStatusResponse>(JsonOptions, ct);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Status))
                throw new KyverumVerifyException("Respuesta de estado inválida de Kyverum.", transient: false);

            // Kyverum permite REINTENTOS dentro de una misma validación (p.ej. 3 intentos). El resultado
            // TERMINAL vive en `result` (aprobado + closedAt); mientras `result` sea null la validación sigue
            // ABIERTA aunque el ÚLTIMO intento (top-level `subjects`) haya sido rechazado. Por eso el estado
            // efectivo se deriva del `result` (o expirado); si no cerró → en_proceso (no se terminaliza por un
            // intento fallido). El score/validadoAt del veredicto vienen de `result.subjects`.
            var subject = SelectStatusSubject(payload.Subjects, parte);
            var resultSubject = SelectStatusSubject(payload.Result?.Subjects, parte);

            // OJO: Kyverum reporta `result` (aprobado/closedAt) tras CADA intento, no solo al agotar los 3.
            // Por eso un `result.aprobado=false` NO es terminal por sí solo → se mapea a `rechazado_intento` y
            // el reconciliador CUENTA los intentos (dedup por validadoAt) para decidir si ya se agotaron.
            string effectiveStatus;
            if (payload.Result?.Aprobado is { } aprobado)
                effectiveStatus = aprobado ? "aprobado" : "rechazado_intento";
            else if (string.Equals(payload.Status, "rechazado", StringComparison.OrdinalIgnoreCase))
                effectiveStatus = "rechazado_intento";
            else
                effectiveStatus = payload.Status!; // en_proceso/enviado/aprobado/expirado (compat)

            var score = resultSubject?.Score ?? subject?.Score;
            var attemptAt = subject?.ValidadoAt ?? resultSubject?.ValidadoAt; // clave de dedup/conteo del intento
            var motivo = subject?.Motivo;
            // Serie/hash del certificado (HU #10488): prioriza el subject del veredicto (result) sobre el
            // del último intento. Kyverum puede no exponerlo en el GET → null (el webhook es la fuente primaria).
            var firmaSerie = resultSubject?.FirmaSerie ?? subject?.FirmaSerie;

            // Trazabilidad SIN OCR/PII: estados/score/fecha (NUNCA datosExtraidos).
            var sanitized = JsonSerializer.Serialize(new
            {
                status = effectiveStatus,
                provider_status = payload.Status,
                result_aprobado = payload.Result?.Aprobado,
                subject_status = subject?.Status,
                // Motivo del ÚLTIMO intento (para mostrar "reintentando: <motivo>" mientras sigue abierta).
                subject_motivo = subject?.Motivo,
                score,
                validado_at = attemptAt, // = AttemptAt: clave de dedup/conteo del intento (consistente)
                firma_serie = firmaSerie,
                proveedor = "kyverum",
            });

            return new KyverumVerifyStatus(effectiveStatus, score, sanitized, attemptAt, motivo, firmaSerie);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new KyverumVerifyException("Timeout consultando el estado en Kyverum.", transient: true);
        }
        catch (HttpRequestException ex)
        {
            throw new KyverumVerifyException($"Error de red consultando el estado en Kyverum: {ex.Message}", transient: true);
        }
        catch (JsonException)
        {
            throw new KyverumVerifyException("No se pudo interpretar el estado de Kyverum.", transient: false);
        }
    }

    /// <summary>Subject de la parte (match por rol, case-insensitive); si no, el primero del arreglo.</summary>
    private static KyverumStatusSubject? SelectStatusSubject(IReadOnlyList<KyverumStatusSubject>? subjects, string? parte)
    {
        if (subjects is not { Count: > 0 })
            return null;
        if (!string.IsNullOrWhiteSpace(parte))
        {
            foreach (var s in subjects)
                if (string.Equals(s.Rol, parte, StringComparison.OrdinalIgnoreCase))
                    return s;
        }
        return subjects[0];
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

    // ── Contrato del GET /v1/validations/{id} (reconciliación) ────────────────
    // `result` es no-null SOLO cuando la validación CERRÓ (aprobado/rechazado final, tras agotar reintentos
    // o aprobar). Mientras es null, la validación sigue abierta (Kyverum permite reintentar). Los `subjects`
    // top-level reflejan el ÚLTIMO intento (puede ser rechazado aunque la validación no haya cerrado).
    private sealed record KyverumStatusResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("result")] KyverumStatusResult? Result,
        [property: JsonPropertyName("subjects")] IReadOnlyList<KyverumStatusSubject>? Subjects);

    private sealed record KyverumStatusResult(
        [property: JsonPropertyName("aprobado")] bool? Aprobado,
        [property: JsonPropertyName("closedAt")] string? ClosedAt,
        [property: JsonPropertyName("subjects")] IReadOnlyList<KyverumStatusSubject>? Subjects);

    private sealed record KyverumStatusSubject(
        [property: JsonPropertyName("rol")] string? Rol,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("score")] int? Score,
        // Mensaje amigable de Kyverum del ÚLTIMO intento (p.ej. "El rostro no es completamente visible…").
        // NO es PII (no trae datosExtraidos); es la guía que ve el cliente para reintentar mejor.
        [property: JsonPropertyName("motivo")] string? Motivo,
        [property: JsonPropertyName("validadoAt")] string? ValidadoAt,
        // Serie/hash del certificado del subject aprobado (HU #10488). Puede no venir en el GET.
        [property: JsonPropertyName("firmaSerie")] string? FirmaSerie = null);
}

/// <summary>Logging source-generated (CA1848) del cliente Kyverum.</summary>
internal static partial class KyverumLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Kyverum respondió error HTTP {StatusCode}. Cuerpo: {ErrorBody}")]
    public static partial void ProviderError(ILogger logger, int statusCode, string errorBody);
}
