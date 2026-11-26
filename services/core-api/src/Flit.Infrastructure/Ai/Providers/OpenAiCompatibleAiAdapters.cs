using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Routing;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Ai.Providers;

/// <summary>Visión común a los adapters OpenAI-compatible: traduce el perfil a los topes del proveedor.</summary>
internal static class OpenAiCompatibleVision
{
    public static Task<AiProviderCallResult> Send(
        OpenAiCompatibleClient client,
        OpenAiCompatibleOptions options,
        string base64,
        string mediaType,
        string prompt,
        string? model,
        int? maxTokens,
        int? timeoutSeconds,
        AiVisionPerfil perfil,
        CancellationToken cancellationToken)
    {
        var (tope, espera) = options.Limites(perfil, maxTokens, timeoutSeconds);
        return client.SendVisionAsync(options, base64, mediaType, prompt, cancellationToken, model, tope, espera);
    }
}

/// <summary>Adapter OpenAI (visión + chat) vía Chat Completions.</summary>
internal sealed class OpenAiAiProviderAdapter(
    OpenAiCompatibleClient client,
    IOptions<OpenAiProviderOptions> options) : IAiVisionProvider, IAiChatProvider
{
    public string ProviderCode => AiProviderCodes.OpenAi;

    public Task<AiProviderCallResult> SendVisionAsync(
        string base64,
        string mediaType,
        string prompt,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null,
        AiVisionPerfil perfil = AiVisionPerfil.Lectura) =>
        OpenAiCompatibleVision.Send(client, options.Value, base64, mediaType, prompt, model, maxTokens, timeoutSeconds, perfil, cancellationToken);

    public Task<AiProviderCallResult> SendChatAsync(
        IReadOnlyList<AiChatSystemBlock> system,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null) =>
        client.SendChatAsync(options.Value, system, turns, cancellationToken, model, maxTokens, timeoutSeconds);
}

/// <summary>Adapter Kimi / Moonshot (visión + chat) OpenAI-compatible.</summary>
internal sealed class KimiAiProviderAdapter(
    OpenAiCompatibleClient client,
    IOptions<KimiProviderOptions> options) : IAiVisionProvider, IAiChatProvider
{
    public string ProviderCode => AiProviderCodes.Kimi;

    public Task<AiProviderCallResult> SendVisionAsync(
        string base64,
        string mediaType,
        string prompt,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null,
        AiVisionPerfil perfil = AiVisionPerfil.Lectura) =>
        OpenAiCompatibleVision.Send(client, options.Value, base64, mediaType, prompt, model, maxTokens, timeoutSeconds, perfil, cancellationToken);

    public Task<AiProviderCallResult> SendChatAsync(
        IReadOnlyList<AiChatSystemBlock> system,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null) =>
        client.SendChatAsync(options.Value, system, turns, cancellationToken, model, maxTokens, timeoutSeconds);
}

/// <summary>Adapter DeepSeek — solo chat (no entra en cola OCR por <see cref="Flit.Admin.Application.Plataforma.Ai.AiProviderCapabilities"/>).</summary>
internal sealed class DeepSeekAiProviderAdapter(
    OpenAiCompatibleClient client,
    IOptions<DeepSeekProviderOptions> options) : IAiChatProvider
{
    public string ProviderCode => AiProviderCodes.DeepSeek;

    public Task<AiProviderCallResult> SendChatAsync(
        IReadOnlyList<AiChatSystemBlock> system,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null) =>
        client.SendChatAsync(options.Value, system, turns, cancellationToken, model, maxTokens, timeoutSeconds);
}
