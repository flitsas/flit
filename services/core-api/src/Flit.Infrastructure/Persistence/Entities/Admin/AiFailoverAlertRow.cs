namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary><c>admin.ai_failover_alerts</c> — alertas in-app 1:1 con transición (Feature #13508).</summary>
internal sealed class AiFailoverAlertRow
{
    public Guid Id { get; set; }

    public Guid TransitionId { get; set; }

    public string ToolCode { get; set; } = string.Empty;

    public string FromProvider { get; set; } = string.Empty;

    public string ToProvider { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? AckedAt { get; set; }

    public Guid? AckedBy { get; set; }
}
