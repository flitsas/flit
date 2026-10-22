using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Routing;
using Flit.Infrastructure.Ocr;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Ai.Providers;

/// <summary>Adapter Anthropic sobre <see cref="AnthropicMessagesClient"/> (visión + chat).</summary>
internal sealed class AnthropicAiProviderAdapter(
    AnthropicMessagesClient client,
    IOptions<AnthropicOptions> options) : IAiVisionProvider, IAiChatProvider
{
    private readonly AnthropicOptions _options = options.Value;

    public string ProviderCode => AiProviderCodes.Anthropic;

    public async Task<AiProviderCallResult> SendVisionAsync(
        string base64,
        string mediaType,
        string prompt,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null)
    {
        var vision = await client
            .SendVisionAsync(base64, mediaType, prompt, cancellationToken, model, maxTokens, timeoutSeconds)
            .ConfigureAwait(false);

        return vision.Ok
            ? AiProviderCallResult.Success(vision.Text!)
            : AiProviderCallResult.Fail(vision.Status, vision.Message);
    }

    public async Task<AiProviderCallResult> SendChatAsync(
        IReadOnlyList<AiChatSystemBlock> system,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null)
    {
        var result = await client
            .SendChatAsync(
                [.. system.Select(b => new AnthropicSystemBlock(b.Text, b.Cache))],
                [.. turns.Select(t => new AnthropicChatTurn(t.Role, t.Text))],
                model ?? _options.DrFlitModel,
                maxTokens ?? _options.DrFlitMaxTokens,
                timeoutSeconds ?? _options.DrFlitTimeoutSeconds,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Ok)
            return AiProviderCallResult.Fail();

        AiProviderTokenUsage? usage = result.Usage is { } u
            ? new AiProviderTokenUsage(
                u.InputTokens,
                u.OutputTokens,
                u.CacheReadInputTokens,
                u.CacheCreationInputTokens)
            : null;

        return AiProviderCallResult.Success(result.Text!, usage);
    }
}
