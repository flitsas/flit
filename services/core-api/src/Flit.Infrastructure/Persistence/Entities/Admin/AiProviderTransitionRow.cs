namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary><c>admin.ai_provider_transitions</c> — append-only (Feature #13508).</summary>
internal sealed class AiProviderTransitionRow
{
    public Guid Id { get; set; }

    public string ToolCode { get; set; } = string.Empty;

    public string? FromProvider { get; set; }

    public string ToProvider { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public Guid? ActorUserId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
