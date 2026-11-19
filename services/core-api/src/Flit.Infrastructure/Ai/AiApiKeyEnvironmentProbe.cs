using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Entrega;
using Microsoft.Extensions.Configuration;

namespace Flit.Infrastructure.Ai;

internal sealed class AiApiKeyEnvironmentProbe(IConfiguration configuration) : IAiApiKeyEnvironmentProbe, ILectorLlaveProveedorIa
{
    private static readonly Dictionary<string, string> EnvVarByProvider =
        new(StringComparer.Ordinal)
        {
            [AiProviderCodes.Anthropic] = "ANTHROPIC_API_KEY",
            [AiProviderCodes.OpenAi] = "OPENAI_API_KEY",
            [AiProviderCodes.Kimi] = "KIMI_API_KEY",
            [AiProviderCodes.DeepSeek] = "DEEPSEEK_API_KEY",
        };

    public bool HasApiKey(string providerCode)
    {
        var key = ReadKeyForMasking(providerCode);
        return !string.IsNullOrWhiteSpace(key);
    }

    /// <summary>ADR-0091: la misma variable por proveedor (DELTA P2=b); solo para sellarla, nunca fuera de Infrastructure.</summary>
    public string? LeerLlave(string providerCode) => ReadKeyForMasking(providerCode);

    public string? ReadKeyForMasking(string providerCode)
    {
        var normalized = AiProviderCodes.Normalize(providerCode);
        if (!EnvVarByProvider.TryGetValue(normalized, out var envName))
        {
            return null;
        }

        var fromConfig = configuration[envName];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable(envName);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv.Trim();
    }
}
