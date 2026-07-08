namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Regla de alerta por umbral — <c>analytics.alert_rules</c> (Reportes 2.0, HU-D §8 del contrato).
/// El scheduler evalúa la métrica en su ventana y dispara si el operador se cumple Y el
/// cooldown venció; al disparar registra un <see cref="AlertEvent"/> y sella
/// <see cref="LastTriggeredAt"/>. Modelo calcado del DDL (sin row_version — ver ReportSchedule).
/// </summary>
public sealed class AlertRule
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>rejection_rate_pct | stuck_count | external_api_errors | pending_identity_validations.</summary>
    public string Metric { get; set; } = string.Empty;

    /// <summary>gt | gte | lt | lte.</summary>
    public string Operator { get; set; } = string.Empty;

    public decimal Threshold { get; set; }

    /// <summary>Ventana de evaluación de la métrica, en minutos (5..43200).</summary>
    public int WindowMinutes { get; set; } = 1440;

    /// <summary>No re-disparar dentro del cooldown, en minutos (5..10080).</summary>
    public int CooldownMinutes { get; set; } = 240;

    /// <summary>Destinatarios (1..10 correos) persistidos como jsonb.</summary>
    public List<string> Recipients { get; set; } = [];

    public bool IsActive { get; set; } = true;

    /// <summary>Último disparo sellado (UTC); ancla del cooldown.</summary>
    public DateTimeOffset? LastTriggeredAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Borrado lógico: no listada, no editable y el scheduler la ignora.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
