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

public sealed record AiProviderTransitionView(
    Guid Id,
    string ToolCode,
    string? FromProvider,
    string ToProvider,
    string Reason,
    string? Detail,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt);

public sealed record AiProviderTransitionsPageView(
    IReadOnlyList<AiProviderTransitionView> Items,
    long TotalCount,
    int Page,
    int PageSize);

public sealed record AiFailoverAlertView(
    Guid Id,
    Guid TransitionId,
    string ToolCode,
    string FromProvider,
    string ToProvider,
    DateTimeOffset OccurredAt,
    bool Acked,
    DateTimeOffset? AckedAt,
    Guid? AckedBy);

public sealed record AiFailoverAlertsListView(IReadOnlyList<AiFailoverAlertView> Items);
