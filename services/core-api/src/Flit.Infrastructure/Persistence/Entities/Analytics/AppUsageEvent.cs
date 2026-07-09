namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Evento de telemetría de uso del aplicativo — <c>analytics.app_usage_events</c>
/// (HU-A Reportes 2.0, contrato §7). Taxonomía cerrada en
/// <see cref="Flit.Infrastructure.Telemetry.UsageEventTypes"/>; sin PII en <see cref="Metadata"/>.
/// </summary>
public sealed class AppUsageEvent
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? UserId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string? Module { get; set; }

    public string? StepKey { get; set; }

    public Guid? ProcedureInstanceId { get; set; }

    public int? DurationMs { get; set; }

    /// <summary>JSON (jsonb) con contexto adicional. PROHIBIDO PII (nombres, documentos, emails, placas, VIN).</summary>
    public string Metadata { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
