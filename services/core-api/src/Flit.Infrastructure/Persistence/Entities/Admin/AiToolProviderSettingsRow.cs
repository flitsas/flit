namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary><c>admin.ai_tool_provider_settings</c> — Feature #13508.</summary>
internal sealed class AiToolProviderSettingsRow
{
    public Guid Id { get; set; }

    public string ToolCode { get; set; } = string.Empty;

    public string ActiveProvider { get; set; } = string.Empty;

    public bool AutoReturnToPrimary { get; set; }

    public string ProvidersJson { get; set; } = "[]";

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
