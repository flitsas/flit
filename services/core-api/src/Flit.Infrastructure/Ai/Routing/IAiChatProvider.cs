namespace Flit.Infrastructure.Ai.Routing;

/// <summary>Adapter de chat (DR. FLIT) identificado por <see cref="ProviderCode"/>.</summary>
internal interface IAiChatProvider
{
    string ProviderCode { get; }

    Task<AiProviderCallResult> SendChatAsync(
        IReadOnlyList<AiChatSystemBlock> system,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null);
}
