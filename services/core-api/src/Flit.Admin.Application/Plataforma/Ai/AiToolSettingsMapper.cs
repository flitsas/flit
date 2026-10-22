using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Admin.Application.Plataforma.Ai;

public static class AiToolSettingsMapper
{
    public static AiToolSettingsView ToView(
        AiToolProviderSettings settings,
        IAiApiKeyEnvironmentProbe keyProbe)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(keyProbe);

        var providers = settings.Providers
            .OrderBy(p => p.PriorityRank)
            .Select(p => ToProviderView(settings.ToolCode, p, keyProbe))
            .ToList();

        return new AiToolSettingsView(
            settings.ToolCode,
            settings.ActiveProvider,
            settings.AutoReturnToPrimary,
            providers,
            settings.RowVersion,
            settings.UpdatedAt ?? settings.CreatedAt,
            settings.UpdatedBy);
    }

    private static AiProviderKeyStatusView ToProviderView(
        string toolCode,
        AiProviderSlot slot,
        IAiApiKeyEnvironmentProbe keyProbe)
    {
        var hasKey = keyProbe.HasApiKey(slot.ProviderCode);
        var mask = hasKey ? AiApiKeyMask.FromKey(keyProbe.ReadKeyForMasking(slot.ProviderCode)) : null;

        return new AiProviderKeyStatusView(
            slot.ProviderCode,
            hasKey,
            mask,
            AiProviderCapabilities.SupportsTool(toolCode, slot.ProviderCode),
            slot.Enabled,
            slot.PriorityRank);
    }
}
