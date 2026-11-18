using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Platform.Sdk.IaConsumo;
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
    ILogger<AnthropicMessagesClient> logger,
    IIaConsumoRegistrador? consumo = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Intentos totales de la llamada HTTP: 1 intento + 1 reintento ante fallo de transporte.</summary>
    private const int MaxAttempts = 2;

    // Mensajes de degradación aptos para mostrar al operador en el wizard.
    private const string MsgManual = "El servicio de lectura automática no está disponible en este momento. Puedes adjuntar el documento manualmente y continuar.";
    private const string MsgTimeout = "La lectura automática tardó demasiado. Puedes adjuntar el documento manualmente y continuar.";
    private const string MsgNoKey = "Servicio de IA no configurado. Adjunta el documento manualmente.";

    private readonly AnthropicOptions _options = options.Value;

    /// <summary>Registro de consumo (ADR-0090). Sin registrador (clientes armados a mano en pruebas) no registra nada.</summary>
    private readonly IIaConsumoRegistrador _consumo = consumo ?? NullIaConsumoRegistrador.Instancia;

    /// <summary>
    /// Envía el documento (base64) + prompt a <c>POST /v1/messages</c> y devuelve el texto del modelo.
    /// Reintenta 1 vez ante fallo de transporte (red/timeout). Ante ausencia de API key, respuesta
    /// no-200, error del proveedor o respuesta inválida devuelve <c>Ok=false</c> con status 503 y un
    /// mensaje de carga manual (degradación graceful). Cada intento HTTP deja una fila en el registro de
    /// consumo de IA (contrato ia-consumo v1), con el ámbito que haya abierto quien llama.
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
        var modelo = model ?? _options.Model;
        var tope = maxTokens ?? _options.MaxTokens;
        // Mismo ámbito para los dos intentos: comparten correlacion_id aunque nadie haya abierto uno afuera.
        using var operacion = _consumo.Contexto.Iniciar(new IaAmbito());
        var medicion = new Medicion(_consumo, IaTiposTarea.Ocr, modelo, tope, prompt, PromptBase: null, mediaType,
            IaConsumoTexto.BytesDeBase64(base64));

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            AnthropicLog.NoKey(logger);
            medicion.Registrar(DateTimeOffset.UtcNow, 1, IaResultados.NoConfigurado, 0, null, "sin_api_key", null, null);
            return new AnthropicVisionResult(false, null, 503, MsgNoKey);
        }

        var blockType = mediaType == "application/pdf" ? "document" : "image";
        var payload = new AnthropicMessagesRequest(
            Model: modelo,
            MaxTokens: tope,
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
            var inicio = DateTimeOffset.UtcNow;
            var intento = (short)attempt;
            var status = 0;
            var meta = new StreamMeta();
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
                status = (int)response.StatusCode;

                // Una respuesta HTTP completa (aun no-200) NO se reintenta: es un fallo del proveedor,
                // no de transporte. Se degrada a 503 con mensaje de carga manual.
                if (!response.StatusCode.Equals(System.Net.HttpStatusCode.OK))
                {
                    AnthropicLog.NonSuccess(logger, status);
                    var (codigo, detalle) = await LeerErrorAsync(response, timeoutCts.Token).ConfigureAwait(false);
                    medicion.Registrar(inicio, intento, IaResultados.DesdeHttp(status), status, null, codigo, detalle, null);
                    return new AnthropicVisionResult(false, null, 503, MsgManual);
                }

                var (text, stopReason, providerError) =
                    await ReadStreamAsync(response, meta, timeoutCts.Token).ConfigureAwait(false);
                meta.StopReason = stopReason;

                // En streaming el proveedor puede fallar DESPUÉS del 200, con un evento `error` a
                // mitad del stream. Sin este caso el fallo pasaría por respuesta vacía.
                if (providerError is not null)
                {
                    AnthropicLog.ProviderError(logger, providerError);
                    medicion.Registrar(inicio, intento, ResultadoDeErrorEnStream(providerError), status, meta, providerError, null, text);
                    return new AnthropicVisionResult(false, null, 503, MsgManual);
                }

                // El tope de salida cortó la respuesta: el JSON viene a media llave y no hay objeto
                // que rescatar. Se avisa aparte porque la salida es subir max_tokens, no reintentar.
                var cortada = string.Equals(stopReason, "max_tokens", StringComparison.Ordinal);
                var invalida = cortada || string.Equals(stopReason, "refusal", StringComparison.Ordinal);
                if (cortada)
                    AnthropicLog.Truncated(logger);

                if (string.IsNullOrWhiteSpace(text))
                {
                    medicion.Registrar(inicio, intento, IaResultados.RespuestaInvalida, status, meta, "respuesta_vacia", null, text);
                    return new AnthropicVisionResult(false, null, 503, MsgManual);
                }

                medicion.Registrar(inicio, intento, invalida ? IaResultados.RespuestaInvalida : IaResultados.Exito, status, meta,
                    invalida ? stopReason : null, null, text);
                return new AnthropicVisionResult(true, text, 200, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                medicion.Registrar(inicio, intento, IaResultados.Cancelado, status, meta, "cancelado", null, null);
                throw;
            }
            catch (TaskCanceledException)
            {
                medicion.Registrar(inicio, intento, IaResultados.Timeout, status, meta, "timeout", null, null);
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
                medicion.Registrar(inicio, intento, IaResultados.Red, status, meta,
                    status == 200 ? "stream_cortado" : ex.GetType().Name, ex.Message, null);
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
                medicion.Registrar(inicio, intento, IaResultados.RespuestaInvalida, status, meta, "json_invalido", null, null);
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
        using var operacion = _consumo.Contexto.Iniciar(new IaAmbito());
        var systemTexto = string.Join("\n\n", system.Select(b => b.Text));
        var medicion = new Medicion(_consumo, IaTiposTarea.Chat, model, maxTokens,
            _consumo.DetalleActivo ? ChatComoTexto(systemTexto, turns.Select(t => (t.Role, t.Text))) : null,
            systemTexto, null, null);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            AnthropicLog.NoKey(logger);
            medicion.Registrar(DateTimeOffset.UtcNow, 1, IaResultados.NoConfigurado, 0, null, "sin_api_key", null, null);
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
            var inicio = DateTimeOffset.UtcNow;
            var intento = (short)attempt;
            var status = 0;
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
                status = (int)response.StatusCode;
                if (!response.StatusCode.Equals(System.Net.HttpStatusCode.OK))
                {
                    AnthropicLog.NonSuccess(logger, status);
                    var (codigo, detalle) = await LeerErrorAsync(response, timeoutCts.Token).ConfigureAwait(false);
                    medicion.Registrar(inicio, intento, IaResultados.DesdeHttp(status), status, null, codigo, detalle, null);
                    return AnthropicChatResult.Failed;
                }

                var body = await response.Content
                    .ReadFromJsonAsync<AnthropicChatResponse>(JsonOptions, timeoutCts.Token)
                    .ConfigureAwait(false);

                var text = string.Concat(
                    (body?.Content ?? []).Where(c => c.Type == "text").Select(c => c.Text));

                var cortada = string.Equals(body?.StopReason, "max_tokens", StringComparison.Ordinal);
                if (cortada)
                    AnthropicLog.Truncated(logger);
                var invalida = cortada || string.Equals(body?.StopReason, "refusal", StringComparison.Ordinal);

                var meta = new StreamMeta
                {
                    Id = body?.Id,
                    Model = body?.Model,
                    StopReason = body?.StopReason,
                    InputTokens = body?.Usage?.InputTokens ?? 0,
                    OutputTokens = body?.Usage?.OutputTokens ?? 0,
                    CacheRead = body?.Usage?.CacheReadInputTokens ?? 0,
                    CacheWrite = body?.Usage?.CacheCreationInputTokens ?? 0,
                };

                if (string.IsNullOrWhiteSpace(text))
                {
                    medicion.Registrar(inicio, intento, IaResultados.RespuestaInvalida, status, meta, "respuesta_vacia", null, text);
                    return AnthropicChatResult.Failed;
                }

                medicion.Registrar(inicio, intento, invalida ? IaResultados.RespuestaInvalida : IaResultados.Exito, status, meta,
                    invalida ? body?.StopReason : null, null, text);
                var usage = body!.Usage is { } u
                    ? new AnthropicUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens)
                    : null;
                return new AnthropicChatResult(true, text, usage);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                medicion.Registrar(inicio, intento, IaResultados.Cancelado, status, null, "cancelado", null, null);
                throw;
            }
            catch (TaskCanceledException)
            {
                medicion.Registrar(inicio, intento, IaResultados.Timeout, status, null, "timeout", null, null);
                if (isLastAttempt)
                {
                    AnthropicLog.Timeout(logger);
                    return AnthropicChatResult.Failed;
                }
                AnthropicLog.Retrying(logger, attempt);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                medicion.Registrar(inicio, intento, IaResultados.Red, status, null, ex.GetType().Name, ex.Message, null);
                if (isLastAttempt)
                {
                    AnthropicLog.Network(logger, ex.Message);
                    return AnthropicChatResult.Failed;
                }
                AnthropicLog.Retrying(logger, attempt);
            }
            catch (JsonException)
            {
                medicion.Registrar(inicio, intento, IaResultados.RespuestaInvalida, status, null, "json_invalido", null, null);
                AnthropicLog.InvalidResponse(logger);
                return AnthropicChatResult.Failed;
            }
        }

        return AnthropicChatResult.Failed;
    }

    /// <summary>Texto de una conversación para <c>ia_consumo_detalle.prompt_texto</c> (solo con el detalle activo).</summary>
    internal static string ChatComoTexto(string system, IEnumerable<(string Role, string Text)> turns)
    {
        var sb = new StringBuilder();
        sb.Append("[system]\n").Append(system);
        foreach (var (role, text) in turns)
            sb.Append("\n\n[").Append(role).Append("]\n").Append(text);
        return sb.ToString();
    }

    /// <summary>
    /// Tipo y mensaje del error del cuerpo de una respuesta no-200 (<c>{"error":{"type":..,"message":..}}</c>). Nunca
    /// lanza por el contenido: si el cuerpo no es ese JSON, devuelve <c>http_&lt;status&gt;</c>.
    /// </summary>
    private static async Task<(string Codigo, string? Mensaje)> LeerErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var porDefecto = $"http_{(int)response.StatusCode}";
#pragma warning disable CA1031 // Leer el detalle del error es una ayuda para el registro; nunca cambia el resultado.
        try
        {
            var cuerpo = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                return (Cadena(error, "type") ?? porDefecto, Cadena(error, "message"));
            }
        }
        catch (Exception)
        {
            // Incluye el corte por deadline mientras se lee el cuerpo: el intento ya terminó en no-200 y se
            // registra una sola vez como tal; leer el detalle nunca debe convertirlo en un timeout aparte.
        }
#pragma warning restore CA1031
        return (porDefecto, null);
    }

    /// <summary>Evento <c>error</c> a mitad del stream → resultado del contrato.</summary>
    private static string ResultadoDeErrorEnStream(string tipo) => tipo switch
    {
        "rate_limit_error" => IaResultados.RateLimit,
        "invalid_request_error" => IaResultados.Rechazado,
        _ => IaResultados.ErrorProveedor,
    };

    /// <summary>Lo que se va sabiendo de la respuesta: id, modelo, motivo de parada y tokens.</summary>
    private sealed class StreamMeta
    {
        public string? Id { get; set; }
        public string? Model { get; set; }
        public string? StopReason { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CacheRead { get; set; }
        public int CacheWrite { get; set; }
    }

    /// <summary>Lo fijo de una llamada (los dos intentos comparten modelo, tope y prompt).</summary>
    private sealed record Medicion(
        IIaConsumoRegistrador Consumo,
        string TipoTarea,
        string Modelo,
        int MaxTokens,
        string? Prompt,
        string? PromptBase,
        string? Mime,
        long? Bytes)
    {
        public void Registrar(
            DateTimeOffset inicio, short intento, string resultado, int status, StreamMeta? meta,
            string? errorCodigo, string? errorMensaje, string? respuesta)
        {
            if (!Consumo.Activo)
                return;
            Consumo.Registrar(new IaLlamada
            {
                Proveedor = IaProveedores.Anthropic,
                TipoTareaPorDefecto = TipoTarea,
                FechaInicio = inicio,
                Intento = intento,
                Resultado = resultado,
                HttpStatus = status,
                ModeloSolicitado = Modelo,
                ModeloRespondido = meta?.Model,
                IdRespuestaProveedor = meta?.Id,
                MaxTokens = MaxTokens,
                StopReason = meta?.StopReason,
                ErrorCodigo = errorCodigo,
                ErrorMensaje = errorMensaje,
                TokensEntrada = meta?.InputTokens ?? 0,
                TokensSalida = meta?.OutputTokens ?? 0,
                TokensCacheLectura = meta?.CacheRead ?? 0,
                TokensCacheEscritura = meta?.CacheWrite ?? 0,
                PromptTexto = Prompt,
                PromptBase = PromptBase,
                RespuestaTexto = Consumo.DetalleActivo ? respuesta : null,
                ArchivoMime = Mime,
                ArchivoBytes = Bytes,
            });
        }
    }

    /// <summary>
    /// Consume el stream SSE y devuelve el texto concatenado de los bloques de texto, el
    /// <c>stop_reason</c> final y el tipo de error del proveedor si llegó uno a mitad del stream.
    /// Id, modelo y tokens (<c>message_start</c> / <c>message_delta</c>) se anotan en <paramref name="meta"/>
    /// para el registro de consumo. Los eventos que no aportan nada (ping, content_block_start/stop) se ignoran.
    /// </summary>
    private static async Task<(string Text, string? StopReason, string? Error)> ReadStreamAsync(
        HttpResponseMessage response,
        StreamMeta meta,
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
                    case "message_start":
                        if (root.TryGetProperty("message", out var started) && started.ValueKind == JsonValueKind.Object)
                        {
                            meta.Id = Cadena(started, "id");
                            meta.Model = Cadena(started, "model");
                            if (started.TryGetProperty("usage", out var u0))
                                AnotarUso(meta, u0);
                        }
                        break;

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
                        // El usage del message_delta es ACUMULADO: trae los output_tokens finales.
                        if (root.TryGetProperty("usage", out var u1))
                            AnotarUso(meta, u1);
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

    private static string? Cadena(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Solo pisa lo que viene con valor: el message_delta trae output_tokens y no siempre repite la entrada.</summary>
    private static void AnotarUso(StreamMeta meta, JsonElement usage)
    {
        if (usage.ValueKind != JsonValueKind.Object)
            return;
        if (Entero(usage, "input_tokens") is { } i && i > 0)
            meta.InputTokens = i;
        if (Entero(usage, "output_tokens") is { } o)
            meta.OutputTokens = o;
        if (Entero(usage, "cache_read_input_tokens") is { } r && r > 0)
            meta.CacheRead = r;
        if (Entero(usage, "cache_creation_input_tokens") is { } w && w > 0)
            meta.CacheWrite = w;
    }

    private static int? Entero(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

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
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("model")] string? Model,
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
