using Flit.Admin.Domain.Ict;

namespace Flit.Admin.Application.Ict;

internal static class IctJobSettingsMapper
{
    public static IctJobSettingsView ToView(IctJobSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DateTimeOffset? updatedAt = settings.UpdatedAt == default ? null : settings.UpdatedAt;
        return new IctJobSettingsView(
            settings.WindowStartHour,
            settings.WindowEndHour,
            settings.BusinessPollSeconds,
            settings.ExternalPollSeconds,
            settings.OrchestratorPollSeconds,
            settings.OrchestratorConcurrency,
            settings.OrchestratorBatchSize,
            settings.SendPollSeconds,
            settings.SendConcurrency,
            settings.SendBatchSize,
            settings.WebhookPollSeconds,
            settings.WebhookBatchSize,
            settings.BusinessBatchSize,
            settings.ExternalBatchSize,
            updatedAt,
            settings.UpdatedBy);
    }
}
