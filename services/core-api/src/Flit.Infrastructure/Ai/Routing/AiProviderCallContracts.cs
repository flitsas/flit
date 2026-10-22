namespace Flit.Infrastructure.Ai.Routing;

/// <summary>Resultado tipado de una llamada a un adapter de proveedor (visión o chat).</summary>
internal sealed record AiProviderCallResult(
    bool Ok,
    string? Text,
    int Status,
    string? Message,
    AiProviderTokenUsage? Usage = null)
{
    public static AiProviderCallResult Fail(int status = 503, string? message = null) =>
        new(false, null, status, message);

    public static AiProviderCallResult Success(string text, AiProviderTokenUsage? usage = null) =>
        new(true, text, 200, null, usage);
}

internal sealed record AiProviderTokenUsage(
    int InputTokens,
    int OutputTokens,
    int CacheReadInputTokens = 0,
    int CacheCreationInputTokens = 0);

/// <summary>Resultado del router tras intentar la cola de proveedores.</summary>
internal sealed record AiRouterResult(
    bool Ok,
    string? Text,
    int Status,
    string? Message,
    string? ProviderCode,
    bool Unavailable,
    AiProviderTokenUsage? Usage = null)
{
    public const string UnavailableErrorCode = "ai_provider_unavailable";

    public static AiRouterResult ProviderUnavailable() =>
        new(false, null, 503, UnavailableErrorCode, null, Unavailable: true);

    public static AiRouterResult Success(
        string text,
        string providerCode,
        AiProviderTokenUsage? usage = null) =>
        new(true, text, 200, null, providerCode, Unavailable: false, usage);

    public static AiRouterResult Fail(int status, string? message, string? providerCode = null) =>
        new(false, null, status, message, providerCode, Unavailable: false);
}

/// <summary>Bloque de system prompt para chat multi-proveedor.</summary>
internal sealed record AiChatSystemBlock(string Text, bool Cache);

/// <summary>Turno de conversación para chat multi-proveedor.</summary>
internal sealed record AiChatTurn(string Role, string Text);
