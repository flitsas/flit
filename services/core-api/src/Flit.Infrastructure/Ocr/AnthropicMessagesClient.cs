using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Ocr;

/// <summary>Resultado de una llamada de visión a Anthropic: texto del modelo, o fallo con código+mensaje.</summary>
internal sealed record AnthropicVisionResult(bool Ok, string? Text, int Status, string? Message);

/// <summary>Bloque del <c>system</c> de una llamada de chat. <paramref name="Cache"/> lo marca cacheable.</summary>
internal sealed record AnthropicSystemBlock(string Text, bool Cache);

/// <summary>Turno de la conversación de una llamada de chat (<c>user</c> o <c>assistant</c>).</summary>
internal sealed record AnthropicChatTurn(string Role, string Text);

/// <summary>Tokens que reporta Anthropic en una respuesta sin streaming.</summary>
internal sealed record AnthropicUsage(int InputTokens, int OutputTokens, int CacheReadInputTokens, int CacheCreationInputTokens);

/// <summary>Resultado de una llamada de chat: texto del modelo y uso de tokens, o fallo.</summary>
internal sealed record AnthropicChatResult(bool Ok, string? Text, AnthropicUsage? Usage)
{
    public static readonly AnthropicChatResult Failed = new(false, null, null);
}

/// <summary>
/// Cliente HTTP resiliente de la Anthropic Messages API para el OCR semántico de documentos de trámites.
/// La respuesta llega SIEMPRE en streaming (SSE): con ~100k tokens de entrada por expediente, una
/// petición sin streaming pasa minutos en silencio y la conexión se corta sola. Timeout, 1 reintento
/// ante fallos de transporte (red/timeout/corte del stream) y degradación
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
    /// <param name="model">Modelo a usar; null → el de <see cref="AnthropicOptions.Model"/> (analizador por tipo).</param>
    /// <param name="maxTokens">Tope de salida; null → el de <see cref="AnthropicOptions.MaxTokens"/>.</param>
    /// <param name="timeoutSeconds">
    /// Deadline de esta llamada; null → <see cref="AnthropicOptions.TimeoutSeconds"/>. El
    /// <c>HttpClient.Timeout</c> se registra con el mayor de los dos deadlines configurados, así que
    /// el corto se impone aquí con un CTS enlazado y sigue tratándose como timeout reintentable.
    /// </param>
    public async Task<AnthropicVisionResult> SendVisionAsync(
        string base64,
        string mediaType,
        string prompt,
        CancellationToken ct,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            AnthropicLog.NoKey(logger);
            return new AnthropicVisionResult(false, null, 503, MsgNoKey);
        }

        var blockType = mediaType == "application/pdf" ? "document" : "image";
        var payload = new AnthropicMessagesRequest(
            Model: model ?? _options.Model,
            MaxTokens: maxTokens ?? _options.MaxTokens,
            Messages:
            [
                new AnthropicMessage("user",
                [
                    new AnthropicContentBlock(blockType, new AnthropicSource("base64", mediaType, base64)),
                    new AnthropicContentBlock("text", Text: prompt),
                ]),
            ]);

        var deadline = TimeSpan.FromSeconds(timeoutSeconds ?? _options.TimeoutSeconds);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var isLastAttempt = attempt == MaxAttempts;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(deadline);

                using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions),
                };
                message.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
                message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

                // ResponseHeadersRead: no se bufferiza el cuerpo, que aquí es un stream SSE que se
                // consume evento a evento.
                using var response = await http.SendAsync(
                    message, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

                // Una respuesta HTTP completa (aun no-200) NO se reintenta: es un fallo del proveedor,
                // no de transporte. Se degrada a 503 con mensaje de carga manual.
                if (!response.StatusCode.Equals(System.Net.HttpStatusCode.OK))
                {
                    AnthropicLog.NonSuccess(logger, (int)response.StatusCode);
                    return new AnthropicVisionResult(false, null, 503, MsgManual);
                }

                var (text, stopReason, providerError) =
                    await ReadStreamAsync(response, timeoutCts.Token).ConfigureAwait(false);

                // En streaming el proveedor puede fallar DESPUÉS del 200, con un evento `error` a
                // mitad del stream. Sin este caso el fallo pasaría por respuesta vacía.
                if (providerError is not null)
                {
                    AnthropicLog.ProviderError(logger, providerError);
                    return new AnthropicVisionResult(false, null, 503, MsgManual);
                }

                // El tope de salida cortó la respuesta: el JSON viene a media llave y no hay objeto
                // que rescatar. Se avisa aparte porque la salida es subir max_tokens, no reintentar.
                if (string.Equals(stopReason, "max_tokens", StringComparison.Ordinal))
                    AnthropicLog.Truncated(logger);

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
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                // Error de red, o el stream se cortó a mitad (IOException). Reintentable: el texto
                // parcial acumulado se descarta y se pide de nuevo. En el último intento, degrada.
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

    /// <summary>
    /// Llamada de chat de texto (DR. FLIT, HU #12918): <c>system</c> en bloques (los marcados con
    /// <see cref="AnthropicSystemBlock.Cache"/> llevan <c>cache_control: ephemeral</c>) y la conversación
    /// en <c>messages</c>. SIN streaming: la salida es una respuesta corta, así que basta el deadline. SIN
    /// <c>tools</c>: el modelo no tiene nada que invocar (guardarraíl ADR-0060 §8.2.2). Mismo reintento que
    /// <see cref="SendVisionAsync"/>: 1 reintento ante fallo de transporte; una respuesta HTTP completa
    /// no-200 no se reintenta. Nunca loguea el texto enviado ni el recibido.
    /// </summary>
    public async Task<AnthropicChatResult> SendChatAsync(
        IReadOnlyList<AnthropicSystemBlock> system,
        IReadOnlyList<AnthropicChatTurn> turns,
        string model,
        int maxTokens,
        int timeoutSeconds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            AnthropicLog.NoKey(logger);
            return AnthropicChatResult.Failed;
        }

        var payload = new AnthropicChatRequest(
            Model: model,
            MaxTokens: maxTokens,
            System: [.. system.Select(b => new AnthropicChatBlock(
                "text", b.Text, b.Cache ? AnthropicCacheControl.Ephemeral : null))],
            Messages: [.. turns.Select(t => new AnthropicChatMessage(t.Role, t.Text))]);

        var deadline = TimeSpan.FromSeconds(timeoutSeconds);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var isLastAttempt = attempt == MaxAttempts;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(deadline);

                using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions),
                };
                message.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
                message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

                using var response = await http.SendAsync(message, timeoutCts.Token).ConfigureAwait(false);
                if (!response.StatusCode.Equals(System.Net.HttpStatusCode.OK))
                {
                    AnthropicLog.NonSuccess(logger, (int)response.StatusCode);
                    return AnthropicChatResult.Failed;
                }

                var body = await response.Content
                    .ReadFromJsonAsync<AnthropicChatResponse>(JsonOptions, timeoutCts.Token)
                    .ConfigureAwait(false);

                var text = string.Concat(
                    (body?.Content ?? []).Where(c => c.Type == "text").Select(c => c.Text));

                if (string.Equals(body?.StopReason, "max_tokens", StringComparison.Ordinal))
                    AnthropicLog.Truncated(logger);

                if (string.IsNullOrWhiteSpace(text))
                    return AnthropicChatResult.Failed;

                var usage = body!.Usage is { } u
                    ? new AnthropicUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens)
                    : null;
                return new AnthropicChatResult(true, text, usage);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                if (isLastAttempt)
                {
                    AnthropicLog.Timeout(logger);
                    return AnthropicChatResult.Failed;
                }
                AnthropicLog.Retrying(logger, attempt);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                if (isLastAttempt)
                {
                    AnthropicLog.Network(logger, ex.Message);
                    return AnthropicChatResult.Failed;
                }
                AnthropicLog.Retrying(logger, attempt);
            }
            catch (JsonException)
            {
                AnthropicLog.InvalidResponse(logger);
                return AnthropicChatResult.Failed;
            }
        }

        return AnthropicChatResult.Failed;
    }

    /// <summary>
    /// Consume el stream SSE y devuelve el texto concatenado de los bloques de texto, el
    /// <c>stop_reason</c> final y el tipo de error del proveedor si llegó uno a mitad del stream.
    /// Los eventos que no aportan texto (ping, content_block_start/stop) se ignoran a propósito.
    /// </summary>
    private static async Task<(string Text, string? StopReason, string? Error)> ReadStreamAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var text = new StringBuilder();
        string? stopReason = null;

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;

                var payload = line["data:".Length..].Trim();
                if (payload.Length == 0)
                    continue;

                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProperty))
                    continue;

                switch (typeProperty.GetString())
                {
                    case "content_block_delta":
                        if (root.TryGetProperty("delta", out var delta)
                            && delta.TryGetProperty("type", out var deltaType)
                            && deltaType.GetString() == "text_delta"
                            && delta.TryGetProperty("text", out var chunk))
                        {
                            text.Append(chunk.GetString());
                        }
                        break;

                    case "message_delta":
                        if (root.TryGetProperty("delta", out var messageDelta)
                            && messageDelta.TryGetProperty("stop_reason", out var reason)
                            && reason.ValueKind == JsonValueKind.String)
                        {
                            stopReason = reason.GetString();
                        }
                        break;

                    case "error":
                        var tipo = root.TryGetProperty("error", out var error)
                            && error.TryGetProperty("type", out var errorType)
                                ? errorType.GetString()
                                : null;
                        return (text.ToString(), stopReason, tipo ?? "unknown");

                    case "message_stop":
                        return (text.ToString(), stopReason, null);
                }
            }
        }

        return (text.ToString(), stopReason, null);
    }

    // ── Contrato Anthropic Messages API (payload de visión) ───────────────────
    /// <param name="Stream">
    /// Siempre true. Una petición de visión con un expediente escaneado lleva ~100k tokens de entrada,
    /// y sin streaming la conexión pasa minutos en silencio esperando la respuesta completa: medido,
    /// un expediente de 9,7 MB moría por «connection reset» a los 2m09s, y el mismo archivo en
    /// streaming resolvía en 19 s.
    /// </param>
    private sealed record AnthropicMessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream = true);

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

    // ── Contrato Anthropic Messages API (payload de chat, sin streaming ni tools) ──
    private sealed record AnthropicChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] IReadOnlyList<AnthropicChatBlock> System,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicChatMessage> Messages);

    private sealed record AnthropicChatBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("cache_control"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AnthropicCacheControl? CacheControl = null);

    private sealed record AnthropicCacheControl([property: JsonPropertyName("type")] string Type)
    {
        public static readonly AnthropicCacheControl Ephemeral = new("ephemeral");
    }

    private sealed record AnthropicChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record AnthropicChatResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<AnthropicChatResponseBlock>? Content,
        [property: JsonPropertyName("stop_reason")] string? StopReason,
        [property: JsonPropertyName("usage")] AnthropicChatResponseUsage? Usage);

    private sealed record AnthropicChatResponseBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);

    private sealed record AnthropicChatResponseUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("cache_read_input_tokens")] int CacheReadInputTokens,
        [property: JsonPropertyName("cache_creation_input_tokens")] int CacheCreationInputTokens);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic OCR: la respuesta se cortó por max_tokens; el JSON llega incompleto")]
    public static partial void Truncated(ILogger logger);
}
