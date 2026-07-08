namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Disparo registrado de una regla de alerta — <c>analytics.alert_events</c> (Reportes 2.0, HU-D
/// §8 del contrato). Fila inmutable creada por el scheduler en la MISMA transacción que sella
/// <c>alert_rules.last_triggered_at</c>; <see cref="Notified"/> se marca tras enviar el correo
/// (best-effort: si el envío falla, el evento queda con notified = false para auditoría).
/// </summary>
public sealed class AlertEvent
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid AlertRuleId { get; set; }

    public DateTimeOffset TriggeredAt { get; set; }

    /// <summary>Valor de la métrica en el momento del disparo.</summary>
    public decimal MetricValue { get; set; }

    /// <summary>Umbral vigente de la regla al disparar (snapshot).</summary>
    public decimal Threshold { get; set; }

    public bool Notified { get; set; }

    /// <summary>Destinatarios notificados (snapshot de la regla) como jsonb.</summary>
    public List<string> Recipients { get; set; } = [];

    /// <summary>Mensaje en español enviado en el correo de la alerta.</summary>
    public string? Message { get; set; }
}
