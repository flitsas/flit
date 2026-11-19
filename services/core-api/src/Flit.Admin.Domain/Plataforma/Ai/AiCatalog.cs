namespace Flit.Admin.Domain.Plataforma.Ai;

/// <summary>Catálogo de herramientas IA (Feature #13508, ADR-0070).</summary>
public static class AiToolCodes
{
    public const string Ocr = "ocr";
    public const string DrFlit = "dr_flit";

    /// <summary>
    /// Redacción del dictamen de Diagnóstico (ADR-0091). Sembrada por el DDL 203; solo admite anthropic
    /// (<c>AiProviderCapabilities</c>). core-api no la ejecuta: entrega su configuración y la llave sellada a core-diagnostico.
    /// </summary>
    public const string DiagnosticoDictamen = "diagnostico_dictamen";

    public static bool IsKnown(string? toolCode) =>
        string.Equals(toolCode, Ocr, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolCode, DrFlit, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolCode, DiagnosticoDictamen, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string toolCode) =>
        string.Equals(toolCode, DrFlit, StringComparison.OrdinalIgnoreCase) ? DrFlit
        : string.Equals(toolCode, Ocr, StringComparison.OrdinalIgnoreCase) ? Ocr
        : string.Equals(toolCode, DiagnosticoDictamen, StringComparison.OrdinalIgnoreCase) ? DiagnosticoDictamen
        : toolCode.Trim().ToLowerInvariant();
}

/// <summary>Proveedores IA soportados en v1.</summary>
public static class AiProviderCodes
{
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";
    public const string Kimi = "kimi";
    public const string DeepSeek = "deepseek";

    public static bool IsKnown(string? providerCode) =>
        providerCode is Anthropic or OpenAi or Kimi or DeepSeek;

    public static string Normalize(string providerCode) => providerCode.Trim().ToLowerInvariant();
}
