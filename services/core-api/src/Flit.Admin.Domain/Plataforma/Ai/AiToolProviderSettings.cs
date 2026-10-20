namespace Flit.Admin.Domain.Plataforma.Ai;

/// <summary>Fila de configuración multi-proveedor por herramienta (<c>admin.ai_tool_provider_settings</c>).</summary>
public sealed record AiToolProviderSettings(
    Guid Id,
    string ToolCode,
    string ActiveProvider,
    bool AutoReturnToPrimary,
    IReadOnlyList<AiProviderSlot> Providers,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy);

/// <summary>Entrada del array <c>providers_json</c> (sin secretos).</summary>
public sealed record AiProviderSlot(string ProviderCode, bool Enabled, int PriorityRank);

/// <summary>Fila append-only de <c>admin.ai_provider_transitions</c> (HU #13512).</summary>
public sealed record AiProviderTransition(
    Guid Id,
    string ToolCode,
    string? FromProvider,
    string ToProvider,
    string Reason,
    string? Detail,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt);

/// <summary>Fila de <c>admin.ai_failover_alerts</c> (HU #13512).</summary>
public sealed record AiFailoverAlert(
    Guid Id,
    Guid TransitionId,
    string ToolCode,
    string FromProvider,
    string ToProvider,
    DateTimeOffset OccurredAt,
    DateTimeOffset? AckedAt,
    Guid? AckedBy);
