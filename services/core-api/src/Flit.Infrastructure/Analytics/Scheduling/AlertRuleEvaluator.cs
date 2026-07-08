namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Decisión PURA de disparo de una regla de alerta (Reportes 2.0, HU-D §8): sin I/O.
/// Dispara si el operador se cumple sobre el valor de la métrica Y el cooldown no está
/// vigente (<c>last_triggered_at</c> null o anterior a <c>now - cooldown</c>).
/// </summary>
public static class AlertRuleEvaluator
{
    /// <summary>¿El valor cumple el operador contra el umbral? Operador desconocido → false.</summary>
    public static bool Matches(string @operator, decimal value, decimal threshold) =>
        @operator switch
        {
            "gt" => value > threshold,
            "gte" => value >= threshold,
            "lt" => value < threshold,
            "lte" => value <= threshold,
            _ => false,
        };

    /// <summary>¿El cooldown sigue vigente? (último disparo dentro de los últimos N minutos).</summary>
    public static bool IsCooldownActive(
        DateTimeOffset? lastTriggeredAtUtc, int cooldownMinutes, DateTimeOffset nowUtc) =>
        lastTriggeredAtUtc is not null
        && lastTriggeredAtUtc.Value > nowUtc.AddMinutes(-cooldownMinutes);

    /// <summary>Decisión completa: operador cumplido Y cooldown vencido (o nunca disparada).</summary>
    public static bool ShouldTrigger(
        string @operator,
        decimal value,
        decimal threshold,
        DateTimeOffset? lastTriggeredAtUtc,
        int cooldownMinutes,
        DateTimeOffset nowUtc) =>
        Matches(@operator, value, threshold)
        && !IsCooldownActive(lastTriggeredAtUtc, cooldownMinutes, nowUtc);
}
