using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Admin.Application.Plataforma.Ai;

/// <summary>Matriz de capacidades v1 (DISENO-TECNICO §3.4).</summary>
public static class AiProviderCapabilities
{
    public static bool SupportsTool(string toolCode, string providerCode)
    {
        var tool = AiToolCodes.Normalize(toolCode);
        var provider = AiProviderCodes.Normalize(providerCode);

        // ADR-0091 (DELTA P1=a): el dictamen de Diagnóstico solo con anthropic (Kimi/DeepSeek sin región declarada;
        // OpenAI exige otro encargado y otro cliente en Diagnóstico).
        if (tool == AiToolCodes.DiagnosticoDictamen)
        {
            return provider == AiProviderCodes.Anthropic;
        }

        if (provider == AiProviderCodes.DeepSeek)
        {
            return tool == AiToolCodes.DrFlit;
        }

        return AiProviderCodes.IsKnown(provider);
    }
}
