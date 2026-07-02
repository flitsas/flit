using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Ocr;

/// <summary>Resultado de una llamada de visión a Anthropic: texto del modelo, o fallo con código+mensaje.</summary>
internal sealed record AnthropicVisionResult(bool Ok, string? Text, int Status, string? Message);

/// <summary>
/// Cliente HTTP resiliente de la Anthropic Messages API para el OCR semántico de documentos de trámites.
/// Timeout (60s vía HttpClient), 1 reintento ante fallos de transporte (red/timeout) y degradación
/// graceful: ante cualquier fallo devuelve 503 con un mensaje usable que invita a adjuntar el documento
/// manualmente. Logging SIN PII: nunca se loguea el binario del documento ni los datos extraídos; solo
/// status/tipo de error. Sigue las convenciones de los clientes HTTP de Infrastructure (typed HttpClient,
/// IOptions, logging source-generated CA1848); la API key sólo viaja en el header <c>x-api-key</c>,
/// nunca en logs. El payload envía el documento como bloque <c>document</c> (PDF) o <c>image</c> en
/// base64 junto al prompt del tipo.
/// </summary>
internal sealed class AnthropicMessagesClient(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicMessagesClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Intentos totales de la llamada HTTP: 1 intento + 1 reintento ante fallo de transporte.</summary>
    private const int MaxAttempts = 2;

    // Mensajes de degradación aptos para mostrar al operador en el wizard.
    private const string MsgManual = "El servicio de lectura automática no está disponible en este momento. Puedes adjuntar el documento manualmente y continuar.";
    private const string MsgTimeout = "La lectura automática tardó demasiado. Puedes adjuntar el documento manualmente y continuar.";
    private const string MsgNoKey = "Servicio de IA no configurado. Adjunta el documento manualmente.";

    private readonly AnthropicOptions _options = options.Value;

    /// <summary>
    /// Envía el documento (base64) + prompt a <c>POST /v1/messages</c> y devuelve el texto del modelo.
    /// Reintenta 1 vez ante fallo de transporte (red/timeout). Ante ausencia de API key, respuesta
    /// no-200, error del proveedor o respuesta inválida devuelve <c>Ok=false</c> con status 503 y un
    /// mensaje de carga manual (degradación graceful).
    /// </summary>
    public async Task<AnthropicVisionResult> SendVisionAsync(
        string base64, string mediaType, string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            AnthropicLog.NoKey(logger);
            return new AnthropicVisionResult(false, null, 503, MsgNoKey);
        }

        var blockType = mediaType == "application/pdf" ? "document" : "image";
        var payload = new AnthropicMessagesRequest(
            Model: _options.Model,
            MaxTokens: _options.MaxTokens,
            Messages:
            [
                new AnthropicMessage("user",
                [
                    new AnthropicContentBlock(blockType, new AnthropicSource("base64", mediaType, base64)),
                    new AnthropicContentBlock("text", Text: prompt),
                ]),
            ]);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var isLastAttempt = attempt == MaxAttempts;
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions),
                };
                message.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
                message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

                using var response = await http.SendAsync(message, ct);

                // Una respuesta HTTP completa (aun no-200) NO se reintenta: es un fallo del proveedor,
                // no de transporte. Se degrada a 503 con mensaje de carga manual.
                if (!response.StatusCode.Equals(System.Net.HttpStatusCode.OK))
                {
                    AnthropicLog.NonSuccess(logger, (int)response.StatusCode);
                    return new AnthropicVisionResult(false, null, 503, MsgManual);
                }

                var body = await response.Content.ReadFromJsonAsync<AnthropicMessagesResponse>(JsonOptions, ct);
                if (body?.Error is not null)
                {
                    AnthropicLog.ProviderError(logger, body.Error.Type ?? "unknown");
                    return new AnthropicVisionResult(false, null, 503, MsgManual);
                }

                var text = FirstText(body);
                if (string.IsNullOrWhiteSpace(text))
                    return new AnthropicVisionResult(false, null, 503, MsgManual);

                return new AnthropicVisionResult(true, text, 200, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                // Timeout del HttpClient: reintentable. En el último intento, degrada.
                if (isLastAttempt)
                {
                    AnthropicLog.Timeout(logger);
                    return new AnthropicVisionResult(false, null, 503, MsgTimeout);
                }
                AnthropicLog.Retrying(logger, attempt);
            }
            catch (HttpRequestException ex)
            {
                // Error de red: reintentable. En el último intento, degrada.
                if (isLastAttempt)
                {
                    AnthropicLog.Network(logger, ex.Message);
                    return new AnthropicVisionResult(false, null, 503, MsgManual);
                }
                AnthropicLog.Retrying(logger, attempt);
            }
            catch (JsonException)
            {
                // Respuesta no interpretable (llegó pero no es JSON válido): no se reintenta.
                AnthropicLog.InvalidResponse(logger);
                return new AnthropicVisionResult(false, null, 503, MsgManual);
            }
        }

        // Inalcanzable: el bucle siempre retorna en el último intento. Red de seguridad.
        return new AnthropicVisionResult(false, null, 503, MsgManual);
    }

    private static string? FirstText(AnthropicMessagesResponse? body)
    {
        if (body?.Content is null)
            return null;
        foreach (var block in body.Content)
        {
            if (block.Type == "text" && !string.IsNullOrEmpty(block.Text))
                return block.Text;
        }
        return null;
    }

    // ── Contrato Anthropic Messages API (payload de visión) ───────────────────
    private sealed record AnthropicMessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<AnthropicContentBlock> Content);

    private sealed record AnthropicContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AnthropicSource? Source = null,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null);

    private sealed record AnthropicSource(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("media_type")] string MediaType,
        [property: JsonPropertyName("data")] string Data);

    private sealed record AnthropicMessagesResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<AnthropicResponseBlock>? Content,
        [property: JsonPropertyName("error")] AnthropicError? Error);

    private sealed record AnthropicResponseBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);

    private sealed record AnthropicError(
        [property: JsonPropertyName("type")] string? Type);
}

/// <summary>Logging source-generated (CA1848) del cliente Anthropic. Nunca loguea imágenes, PDFs ni datos extraídos.</summary>
internal static partial class AnthropicLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic OCR: API key no configurada")]
    public static partial void NoKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic OCR respondió HTTP {StatusCode}")]
    public static partial void NonSuccess(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic OCR payload de error tipo {ErrorType}")]
    public static partial void ProviderError(ILogger logger, string errorType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Anthropic OCR: reintentando tras fallo de transporte (intento {Attempt})")]
    public static partial void Retrying(ILogger logger, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic OCR timeout tras esperar la respuesta")]
    public static partial void Timeout(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic OCR error de red: {Detail}")]
    public static partial void Network(ILogger logger, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic OCR: respuesta no interpretable (JSON)")]
    public static partial void InvalidResponse(ILogger logger);
}
