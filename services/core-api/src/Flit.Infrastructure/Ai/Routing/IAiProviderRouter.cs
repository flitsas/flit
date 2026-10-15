namespace Flit.Infrastructure.Ai.Routing;

/// <summary>
/// Router multi-proveedor con failover en la misma request (Feature #13508 / HU #13510, ADR-0070).
/// </summary>
internal interface IAiProviderRouter
{
    Task<AiRouterResult> InvokeVisionAsync(
        string toolCode,
        string base64,
        string mediaType,
        string prompt,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null);

    Task<AiRouterResult> InvokeChatAsync(
        string toolCode,
        IReadOnlyList<AiChatSystemBlock> system,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null);
}
