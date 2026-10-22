namespace Flit.Infrastructure.Ai.Providers;

/// <summary>Opciones de un proveedor OpenAI-compatible (openai / kimi / deepseek). Secretos nunca se loguean.</summary>
internal class OpenAiCompatibleOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Modelo de chat (DR. FLIT).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Modelo de visión (OCR). Si vacío, se usa <see cref="Model"/>.</summary>
    public string VisionModel { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxTokens { get; set; } = 2000;

    public int DrFlitMaxTokens { get; set; } = 600;

    public int DrFlitTimeoutSeconds { get; set; } = 20;

    public string ResolveVisionModel() =>
        string.IsNullOrWhiteSpace(VisionModel) ? Model : VisionModel;
}

/// <summary>Marcadores de opciones tipadas para DI (<c>IOptions&lt;T&gt;</c>).</summary>
internal sealed class OpenAiProviderOptions : OpenAiCompatibleOptions
{
    public const string SectionName = "OpenAI";
}

internal sealed class KimiProviderOptions : OpenAiCompatibleOptions
{
    public const string SectionName = "Kimi";
}

internal sealed class DeepSeekProviderOptions : OpenAiCompatibleOptions
{
    public const string SectionName = "DeepSeek";
}
