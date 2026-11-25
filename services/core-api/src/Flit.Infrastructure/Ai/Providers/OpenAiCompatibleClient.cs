using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Infrastructure.Ai.Routing;
using Flit.Platform.Sdk.IaConsumo;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Ai.Providers;

/// <summary>
/// Cliente HTTP de Chat Completions (OpenAI-compatible) para openai / kimi / deepseek.
/// Un intento + 1 reintento ante fallo de transporte; nunca loguea prompts ni secretos.
/// </summary>
internal sealed class OpenAiCompatibleClient(
    HttpClient http,
    ILogger<OpenAiCompatibleClient> logger,
    IIaConsumoRegistrador? consumo = null)
{
    /// <summary>Registro de consumo (ADR-0090): una fila por intento HTTP. Sin registrador no registra nada.</summary>
    private readonly IIaConsumoRegistrador _consumo = consumo ?? NullIaConsumoRegistrador.Instancia;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const int MaxAttempts = 2;
    private const string MsgManual = "El servicio de lectura automática no está disponible en este momento. Puedes adjuntar el documento manualmente y continuar.";
    private const string MsgTimeout = "La lectura automática tardó demasiado. Puedes adjuntar el documento manualmente y continuar.";
    private const string MsgNoKey = "Servicio de IA no configurado. Adjunta el documento manualmente.";

    public Task<AiProviderCallResult> SendVisionAsync(
        OpenAiCompatibleOptions options,
        string base64,
        string mediaType,
        string prompt,
        CancellationToken ct,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null)
    {
        var dataUrl = $"data:{mediaType};base64,{base64}";
        var content = new object[]
        {
            new OpenAiTextPart("text", prompt),
            new OpenAiImagePart("image_url", new OpenAiImageUrl(dataUrl)),
        };

        var payload = new OpenAiChatRequest(
            Model: model ?? options.ResolveVisionModel(),
            MaxTokens: maxTokens ?? options.MaxTokens,
            Messages: [new OpenAiMessage("user", content)]);

        return SendAsync(options, payload, timeoutSeconds ?? options.TimeoutSeconds, IaTiposTarea.Ocr,
            prompt, promptBase: null, mediaType, IaConsumoTexto.BytesDeBase64(base64), ct);
    }

    public Task<AiProviderCallResult> SendChatAsync(
        OpenAiCompatibleOptions options,
        IReadOnlyList<AiChatSystemBlock> system,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken ct,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null)
    {
        var messages = new List<OpenAiMessage>(system.Count + turns.Count);
        foreach (var block in system)
            messages.Add(new OpenAiMessage("system", block.Text));

        foreach (var turn in turns)
            messages.Add(new OpenAiMessage(turn.Role, turn.Text));

        var payload = new OpenAiChatRequest(
            Model: model ?? options.Model,
            MaxTokens: maxTokens ?? options.DrFlitMaxTokens,
            Messages: messages);

        var systemTexto = string.Join("\n\n", system.Select(b => b.Text));
        var promptTexto = _consumo.DetalleActivo
            ? Flit.Infrastructure.Ocr.AnthropicMessagesClient.ChatComoTexto(systemTexto, turns.Select(t => (t.Role, t.Text)))
            : null;
        return SendAsync(options, payload, timeoutSeconds ?? options.DrFlitTimeoutSeconds, IaTiposTarea.Chat,
            promptTexto, systemTexto, mime: null, bytes: null, ct);
    }

    private async Task<AiProviderCallResult> SendAsync(
        OpenAiCompatibleOptions options,
        OpenAiChatRequest payload,
        int timeoutSeconds,
        string tipoTarea,
        string? promptTexto,
        string? promptBase,
        string? mime,
        long? bytes,
        CancellationToken ct)
    {
        // Mismo ámbito para los dos intentos: comparten correlacion_id aunque nadie haya abierto uno afuera.
        using var operacion = _consumo.Contexto.Iniciar(new IaAmbito());
        var medicion = new Medicion(_consumo, options.CodigoProveedor, tipoTarea, payload.Model, payload.MaxTokens,
            promptTexto, promptBase, mime, bytes);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            OpenAiCompatLog.NoKey(logger);
            medicion.Registrar(DateTimeOffset.UtcNow, 1, IaResultados.NoConfigurado, 0, null, "sin_api_key", null, null);
            return AiProviderCallResult.Fail(503, MsgNoKey);
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            OpenAiCompatLog.NoBaseUrl(logger);
            medicion.Registrar(DateTimeOffset.UtcNow, 1, IaResultados.NoConfigurado, 0, null, "sin_base_url", null, null);
            return AiProviderCallResult.Fail(503, MsgManual);
        }

        var endpoint = new Uri(CombineUrl(options.BaseUrl, "chat/completions"));
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

                using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(payload, options: JsonOptions),
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

                using var response = await http
                    .SendAsync(message, timeoutCts.Token)
                    .ConfigureAwait(false);
                status = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    OpenAiCompatLog.NonSuccess(logger, status);
                    var (codigo, detalle) = await LeerErrorAsync(response, timeoutCts.Token).ConfigureAwait(false);
                    medicion.Registrar(inicio, intento, IaResultados.DesdeHttp(status), status, null, codigo, detalle, null);
                    return AiProviderCallResult.Fail(503, MsgManual);
                }

                var body = await response.Content
                    .ReadFromJsonAsync<OpenAiChatResponse>(JsonOptions, timeoutCts.Token)
                    .ConfigureAwait(false);

                var choice = body?.Choices is { Count: > 0 } choices ? choices[0] : null;
                var text = choice?.Message?.Content;
                var cortada = string.Equals(choice?.FinishReason, "length", StringComparison.Ordinal);

                if (string.IsNullOrWhiteSpace(text))
                {
                    medicion.Registrar(inicio, intento, IaResultados.RespuestaInvalida, status, body, "respuesta_vacia", null, text);
                    return AiProviderCallResult.Fail(503, MsgManual);
                }

                medicion.Registrar(inicio, intento, cortada ? IaResultados.RespuestaInvalida : IaResultados.Exito, status, body,
                    cortada ? "length" : null, null, text);

                AiProviderTokenUsage? usage = body?.Usage is { } u
                    ? new AiProviderTokenUsage(u.PromptTokens, u.CompletionTokens)
                    : null;

                return AiProviderCallResult.Success(text, usage);
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
                    OpenAiCompatLog.Timeout(logger);
                    return AiProviderCallResult.Fail(503, MsgTimeout);
                }

                OpenAiCompatLog.Retrying(logger, attempt);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                medicion.Registrar(inicio, intento, IaResultados.Red, status, null, ex.GetType().Name, ex.Message, null);
                if (isLastAttempt)
                {
                    OpenAiCompatLog.Network(logger, ex.Message);
                    return AiProviderCallResult.Fail(503, MsgManual);
                }

                OpenAiCompatLog.Retrying(logger, attempt);
            }
            catch (JsonException)
            {
                medicion.Registrar(inicio, intento, IaResultados.RespuestaInvalida, status, null, "json_invalido", null, null);
                OpenAiCompatLog.InvalidResponse(logger);
                return AiProviderCallResult.Fail(503, MsgManual);
            }
        }

        return AiProviderCallResult.Fail(503, MsgManual);
    }

    /// <summary>Código y mensaje del cuerpo de error OpenAI-compatible (<c>{"error":{"code"|"type", "message"}}</c>).</summary>
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
                var codigo = Cadena(error, "code") ?? Cadena(error, "type");
                return (codigo ?? porDefecto, Cadena(error, "message"));
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

    private static string? Cadena(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Lo fijo de una llamada (los dos intentos comparten proveedor, modelo, tope y prompt).</summary>
    private sealed record Medicion(
        IIaConsumoRegistrador Consumo,
        string Proveedor,
        string TipoTarea,
        string Modelo,
        int MaxTokens,
        string? Prompt,
        string? PromptBase,
        string? Mime,
        long? Bytes)
    {
        public void Registrar(
            DateTimeOffset inicio, short intento, string resultado, int status, OpenAiChatResponse? body,
            string? errorCodigo, string? errorMensaje, string? respuesta)
        {
            if (!Consumo.Activo)
                return;
            // OpenAI cuenta los tokens cacheados DENTRO de prompt_tokens (DeepSeek los da aparte en
            // prompt_cache_hit_tokens, también incluidos): se separan para no cobrarlos dos veces.
            var u = body?.Usage;
            var cacheados = u?.PromptTokensDetails?.CachedTokens ?? u?.PromptCacheHitTokens ?? 0;
            var entrada = Math.Max(0, (u?.PromptTokens ?? 0) - cacheados);
            Consumo.Registrar(new IaLlamada
            {
                Proveedor = Proveedor,
                TipoTareaPorDefecto = TipoTarea,
                FechaInicio = inicio,
                Intento = intento,
                Resultado = resultado,
                HttpStatus = status,
                ModeloSolicitado = Modelo,
                ModeloRespondido = body?.Model,
                IdRespuestaProveedor = body?.Id,
                MaxTokens = MaxTokens,
                StopReason = body?.Choices is { Count: > 0 } c ? c[0].FinishReason : null,
                ErrorCodigo = errorCodigo,
                ErrorMensaje = errorMensaje,
                TokensEntrada = entrada,
                TokensSalida = u?.CompletionTokens ?? 0,
                TokensCacheLectura = cacheados,
                PromptTexto = Prompt,
                PromptBase = PromptBase,
                RespuestaTexto = Consumo.DetalleActivo ? respuesta : null,
                ArchivoMime = Mime,
                ArchivoBytes = Bytes,
            });
        }
    }

    private static string CombineUrl(string baseUrl, string relative)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return $"{trimmed}/{relative.TrimStart('/')}";
    }

    private sealed record OpenAiChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<OpenAiMessage> Messages);

    private sealed record OpenAiMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] object Content);

    private sealed record OpenAiTextPart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record OpenAiImagePart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("image_url")] OpenAiImageUrl ImageUrl);

    private sealed record OpenAiImageUrl(
        [property: JsonPropertyName("url")] string Url);

    private sealed record OpenAiChatResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChoice>? Choices,
        [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

    private sealed record OpenAiChoice(
        [property: JsonPropertyName("message")] OpenAiResponseMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason = null);

    private sealed record OpenAiResponseMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record OpenAiUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
        [property: JsonPropertyName("prompt_tokens_details")] OpenAiPromptTokensDetails? PromptTokensDetails = null,
        [property: JsonPropertyName("prompt_cache_hit_tokens")] int? PromptCacheHitTokens = null);

    private sealed record OpenAiPromptTokensDetails(
        [property: JsonPropertyName("cached_tokens")] int? CachedTokens);
}

internal static partial class OpenAiCompatLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI-compatible: API key no configurada")]
    public static partial void NoKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI-compatible: BaseUrl no configurada")]
    public static partial void NoBaseUrl(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI-compatible respondió HTTP {StatusCode}")]
    public static partial void NonSuccess(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenAI-compatible: reintentando tras fallo de transporte (intento {Attempt})")]
    public static partial void Retrying(ILogger logger, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI-compatible timeout tras esperar la respuesta")]
    public static partial void Timeout(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI-compatible error de red: {Detail}")]
    public static partial void Network(ILogger logger, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI-compatible: respuesta no interpretable (JSON)")]
    public static partial void InvalidResponse(ILogger logger);
}
