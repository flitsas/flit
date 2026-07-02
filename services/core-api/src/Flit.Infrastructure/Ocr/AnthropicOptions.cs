namespace Flit.Infrastructure.Ocr;

/// <summary>
/// Configuración del cliente Anthropic Messages API para el OCR de trámites. Valores desde
/// <c>Anthropic:*</c> (config) con fallback a env <c>ANTHROPIC_*</c> — mismo patrón que el resto de
/// integraciones externas de la casa. La API key NUNCA se loguea ni se expone en DTOs.
/// </summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Modelo de visión (Claude Haiku), configurable por entorno.</summary>
    public string Model { get; set; } = "claude-haiku-4-5-20251001";

    public int TimeoutSeconds { get; set; } = 60;
    public int MaxTokens { get; set; } = 2000;
}
