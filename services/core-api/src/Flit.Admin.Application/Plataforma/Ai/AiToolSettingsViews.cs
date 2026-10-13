namespace Flit.Admin.Application.Plataforma.Ai;

public sealed record AiToolsListView(IReadOnlyList<AiToolSettingsView> Tools);

public sealed record AiToolSettingsView(
    string ToolCode,
    string ActiveProvider,
    bool AutoReturnToPrimary,
    IReadOnlyList<AiProviderKeyStatusView> Providers,
    long RowVersion,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy);

public sealed record AiProviderKeyStatusView(
    string ProviderCode,
    bool HasApiKey,
    string? ApiKeyMask,
    bool Capable,
    bool Enabled,
    int PriorityRank,
    string Health = "unknown");

public sealed record SaveAiToolSettingsCommand(
    bool AutoReturnToPrimary,
    IReadOnlyList<AiProviderSlotCommand> Providers,
    Guid? ActorUserId);

public sealed record AiProviderSlotCommand(string ProviderCode, bool Enabled, int PriorityRank);

public sealed record SaveAiToolSettingsResult(bool IsValid, AiToolSettingsView? View, string? ErrorCode);

public sealed record ActivateAiProviderCommand(string ProviderCode, string? Reason, Guid? ActorUserId);

public sealed record ActivateAiProviderResult(
    ActivateAiProviderOutcome Outcome,
    AiToolSettingsView? View);

public enum ActivateAiProviderOutcome
{
    Ok,
    ToolNotFound,
    UnknownProvider,
    ProviderDisabled,
    ProviderNotCapable,
    ProviderNoApiKey,
    Unchanged,
}
