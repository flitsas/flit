using Flit.Admin.Domain.Plataforma.Ai;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.Chat;
using Flit.Infrastructure.Ai.Routing;
using Flit.Infrastructure.Ocr;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Chat DR. FLIT vía <see cref="IAiProviderRouter"/> con failover (HU #13510).
/// Respeta <c>Anthropic:DrFlitEnabled</c> (apagado total sin HTTP).
/// </summary>
internal sealed class RoutedDrFlitChatModel(
    IAiProviderRouter router,
    IOptions<AnthropicOptions> options) : IDrFlitChatModel
{
    private readonly AnthropicOptions _options = options.Value;

    public async Task<DrFlitModelCallResult> CompleteAsync(
        DrFlitSystemPrompt system,
        IReadOnlyList<DrFlitTurn> turns,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(turns);

        if (!_options.DrFlitEnabled)
            return DrFlitModelCallResult.Disabled();

        var routed = await router
            .InvokeChatAsync(
                AiToolCodes.DrFlit,
                [
                    new AiChatSystemBlock(system.ManualBlock, Cache: true),
                    new AiChatSystemBlock(system.Instructions, Cache: true),
                ],
                [.. turns.Select(t => new AiChatTurn(
                    t.Role == DrFlitTurnRole.User ? "user" : "assistant",
                    t.Text))],
                ct,
                model: null,
                maxTokens: _options.DrFlitMaxTokens,
                timeoutSeconds: _options.DrFlitTimeoutSeconds)
            .ConfigureAwait(false);

        if (!routed.Ok)
            return DrFlitModelCallResult.Failed();

        var usage = routed.Usage is { } u
            ? new DrFlitTokenUsage(
                u.InputTokens,
                u.OutputTokens,
                u.CacheReadInputTokens,
                u.CacheCreationInputTokens)
            : null;

        return new DrFlitModelCallResult(DrFlitModelCallStatus.Ok, routed.Text, usage);
    }
}
