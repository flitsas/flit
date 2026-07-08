namespace Flit.Analytics.Application.Scheduling;

/// <summary>
/// Evaluación puntual de las métricas de alerta (HU-D Reportes 2.0, §8 del contrato).
/// Implementada en Flit.Infrastructure con SQL propio; consumida por el scheduler.
/// Métricas soportadas (vocabulario cerrado de <c>analytics.alert_rules.metric</c>):
/// <list type="bullet">
/// <item><c>rejection_rate_pct</c>: rechazados / (aprobados + rechazados) * 100 sobre las
///   transiciones ocurridas dentro de la ventana.</item>
/// <item><c>stuck_count</c>: instancias en estado no final sin transición hace más de 7 días
///   (stuckDays = 7 fijo; la ventana no aplica).</item>
/// <item><c>external_api_errors</c>: llamadas con error en <c>admin.ot_api_call_logs</c>
///   dentro de la ventana (response_code &gt;= 400 o error_message no nulo).</item>
/// <item><c>pending_identity_validations</c>: validaciones biométricas cuyo estado ACTUAL es
///   pendiente/en proceso (la ventana no aplica).</item>
/// </list>
/// </summary>
public interface IAlertMetricsReadRepository
{
    Task<decimal> GetMetricValueAsync(
        Guid tenantId, string metric, int windowMinutes, CancellationToken ct);
}
