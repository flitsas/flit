using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Infrastructure.Ai.Routing;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Ai.Providers;

/// <summary>
/// Cliente HTTP de Chat Completions (OpenAI-compatible) para openai / kimi / deepseek.
/// Un intento + 1 reintento ante fallo de transporte; nunca loguea prompts ni secretos.
/// </summary>
internal sealed class OpenAiCompatibleClient(
    HttpClient http,
    ILogger<OpenAiCompatibleClient> logger)
{
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

        return SendAsync(options, payload, timeoutSeconds ?? options.TimeoutSeconds, ct);
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

        return SendAsync(options, payload, timeoutSeconds ?? options.DrFlitTimeoutSeconds, ct);
    }

    private async Task<AiProviderCallResult> SendAsync(
        OpenAiCompatibleOptions options,
        OpenAiChatRequest payload,
        int timeoutSeconds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            OpenAiCompatLog.NoKey(logger);
            return AiProviderCallResult.Fail(503, MsgNoKey);
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            OpenAiCompatLog.NoBaseUrl(logger);
            return AiProviderCallResult.Fail(503, MsgManual);
        }

        var endpoint = new Uri(CombineUrl(options.BaseUrl, "chat/completions"));
        var deadline = TimeSpan.FromSeconds(timeoutSeconds);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var isLastAttempt = attempt == MaxAttempts;
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

                if (!response.IsSuccessStatusCode)
                {
                    OpenAiCompatLog.NonSuccess(logger, (int)response.StatusCode);
                    return AiProviderCallResult.Fail(503, MsgManual);
                }

                var body = await response.Content
                    .ReadFromJsonAsync<OpenAiChatResponse>(JsonOptions, timeoutCts.Token)
                    .ConfigureAwait(false);

                var text = body?.Choices is { Count: > 0 } choices
                    ? choices[0].Message?.Content
                    : null;

                if (string.IsNullOrWhiteSpace(text))
                    return AiProviderCallResult.Fail(503, MsgManual);

                AiProviderTokenUsage? usage = body?.Usage is { } u
                    ? new AiProviderTokenUsage(u.PromptTokens, u.CompletionTokens)
                    : null;

                return AiProviderCallResult.Success(text, usage);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                if (isLastAttempt)
                {
                    OpenAiCompatLog.Timeout(logger);
                    return AiProviderCallResult.Fail(503, MsgTimeout);
                }

                OpenAiCompatLog.Retrying(logger, attempt);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                if (isLastAttempt)
                {
                    OpenAiCompatLog.Network(logger, ex.Message);
                    return AiProviderCallResult.Fail(503, MsgManual);
                }

                OpenAiCompatLog.Retrying(logger, attempt);
            }
            catch (JsonException)
            {
                OpenAiCompatLog.InvalidResponse(logger);
                return AiProviderCallResult.Fail(503, MsgManual);
            }
        }

        return AiProviderCallResult.Fail(503, MsgManual);
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
        [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChoice>? Choices,
        [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

    private sealed record OpenAiChoice(
        [property: JsonPropertyName("message")] OpenAiResponseMessage? Message);

    private sealed record OpenAiResponseMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record OpenAiUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
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
