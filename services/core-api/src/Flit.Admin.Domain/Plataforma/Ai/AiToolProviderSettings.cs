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
