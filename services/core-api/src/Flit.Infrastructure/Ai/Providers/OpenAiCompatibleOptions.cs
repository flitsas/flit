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

    /// <summary>
    /// Tope de salida de la sonda de orientación (<see cref="Routing.AiVisionPerfil.Sonda"/>). La respuesta
    /// es <c>{"derecha":true}</c>; un modelo que razona (kimi-k3) necesita bastante más para llegar a ella.
    /// </summary>
    public int OrientacionMaxTokens { get; set; } = 100;

    /// <summary>Deadline de cada sonda de orientación: va en el camino crítico de cada carga.</summary>
    public int OrientacionTimeoutSeconds { get; set; } = 30;

    /// <summary>Resuelve tope y deadline según el perfil; lo explícito del llamador siempre gana.</summary>
    public (int? MaxTokens, int? TimeoutSeconds) Limites(
        Routing.AiVisionPerfil perfil, int? maxTokens, int? timeoutSeconds) =>
        perfil == Routing.AiVisionPerfil.Sonda
            ? (maxTokens ?? OrientacionMaxTokens, timeoutSeconds ?? OrientacionTimeoutSeconds)
            : (maxTokens, timeoutSeconds);

    /// <summary>Código del proveedor para el registro de consumo de IA (ADR-0090).</summary>
    public virtual string CodigoProveedor => Flit.Platform.Sdk.IaConsumo.IaProveedores.OpenAi;

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

    public override string CodigoProveedor => Flit.Platform.Sdk.IaConsumo.IaProveedores.Kimi;
}

internal sealed class DeepSeekProviderOptions : OpenAiCompatibleOptions
{
    public const string SectionName = "DeepSeek";

    public override string CodigoProveedor => Flit.Platform.Sdk.IaConsumo.IaProveedores.DeepSeek;
}
