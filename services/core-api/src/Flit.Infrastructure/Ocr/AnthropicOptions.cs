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

    // ── Clasificación del cargue masivo ──────────────────────────────────────
    // Decidir QUÉ documento hay en cada página de un expediente escaneado es bastante más difícil que
    // verificar un tipo ya conocido, así que ese paso corre en el modelo fuerte. Se paga una vez por
    // archivo; los recortes que salen de ahí los sigue verificando Haiku con los prompts por tipo.

    /// <summary>Modelo del clasificador de lote. Configurable para poder bajarlo sin desplegar.</summary>
    public string ClassifierModel { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// Tope de salida del clasificador. Holgado a propósito: en Sonnet 5 el <i>thinking</i> adaptativo
    /// está activo por defecto y <c>max_tokens</c> limita razonamiento + respuesta juntos, así que un
    /// tope corto trunca el JSON a mitad de camino.
    /// </summary>
    public int ClassifierMaxTokens { get; set; } = 8000;

    /// <summary>
    /// Timeout del clasificador. Mayor que el del analizador: clasificar un expediente de 30 páginas
    /// escaneadas es una sola llamada, pero larga.
    /// </summary>
    public int ClassifierTimeoutSeconds { get; set; } = 180;
}
