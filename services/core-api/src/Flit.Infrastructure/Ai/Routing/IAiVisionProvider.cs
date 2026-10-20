namespace Flit.Infrastructure.Ai.Routing;

/// <summary>Adapter de visión (OCR) identificado por <see cref="ProviderCode"/>.</summary>
internal interface IAiVisionProvider
{
    string ProviderCode { get; }

    Task<AiProviderCallResult> SendVisionAsync(
        string base64,
        string mediaType,
        string prompt,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null);
}
