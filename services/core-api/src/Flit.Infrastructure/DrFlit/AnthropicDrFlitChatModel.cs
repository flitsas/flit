using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.Chat;
using Flit.Infrastructure.Ocr;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Adaptador del chat de DR. FLIT sobre <see cref="AnthropicMessagesClient"/> (HU #12918, ADR-0060 §6.1).
/// Toma modelo, tope y deadline de <c>Anthropic:DrFlit*</c>; el manual y las instrucciones van como dos
/// bloques cacheables del <c>system</c>, en ese orden, y la conversación en <c>messages</c>.
/// </summary>
internal sealed class AnthropicDrFlitChatModel(
    AnthropicMessagesClient client,
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

        // AC4 — apagado del LLM: ni una llamada HTTP. Tampoco tiene sentido intentarlo sin API key.
        if (!_options.DrFlitEnabled || string.IsNullOrWhiteSpace(_options.ApiKey))
            return DrFlitModelCallResult.Disabled();

        var result = await client.SendChatAsync(
            [new AnthropicSystemBlock(system.ManualBlock, Cache: true), new AnthropicSystemBlock(system.Instructions, Cache: true)],
            [.. turns.Select(t => new AnthropicChatTurn(t.Role == DrFlitTurnRole.User ? "user" : "assistant", t.Text))],
            _options.DrFlitModel,
            _options.DrFlitMaxTokens,
            _options.DrFlitTimeoutSeconds,
            ct).ConfigureAwait(false);

        if (!result.Ok)
            return DrFlitModelCallResult.Failed();

        var usage = result.Usage is { } u
            ? new DrFlitTokenUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens)
            : null;
        return new DrFlitModelCallResult(DrFlitModelCallStatus.Ok, result.Text, usage);
    }
}
