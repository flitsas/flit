using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Infrastructure.Ai.Routing;

/// <summary>
/// Construye la cola de failover: enabled ∩ HasApiKey ∩ SupportsTool, orden priorityRank ASC,
/// con <paramref name="activeProvider"/> primero si está entre los candidatos.
/// </summary>
internal static class AiProviderQueue
{
    internal static readonly IReadOnlyList<AiProviderSlot> DefaultSlots =
    [
        new(AiProviderCodes.Anthropic, Enabled: true, PriorityRank: 1),
        new(AiProviderCodes.OpenAi, Enabled: true, PriorityRank: 2),
        new(AiProviderCodes.Kimi, Enabled: true, PriorityRank: 3),
        new(AiProviderCodes.DeepSeek, Enabled: true, PriorityRank: 4),
    ];

    public static IReadOnlyList<string> Build(
        string toolCode,
        string? activeProvider,
        IReadOnlyList<AiProviderSlot> providers,
        Func<string, bool> hasApiKey)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(hasApiKey);

        var normalizedTool = AiToolCodes.Normalize(toolCode);
        var slots = providers.Count > 0 ? providers : DefaultSlots;

        var candidates = slots
            .Where(p => p.Enabled)
            .Select(p => new
            {
                Code = AiProviderCodes.Normalize(p.ProviderCode),
                p.PriorityRank,
            })
            .Where(p => AiProviderCodes.IsKnown(p.Code))
            .Where(p => AiProviderCapabilities.SupportsTool(normalizedTool, p.Code))
            .Where(p => hasApiKey(p.Code))
            .OrderBy(p => p.PriorityRank)
            .Select(p => p.Code)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!string.IsNullOrWhiteSpace(activeProvider))
        {
            var active = AiProviderCodes.Normalize(activeProvider);
            var idx = candidates.FindIndex(c => string.Equals(c, active, StringComparison.Ordinal));
            if (idx > 0)
            {
                candidates.RemoveAt(idx);
                candidates.Insert(0, active);
            }
        }

        return candidates;
    }
}
