using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Domain.Abstractions;

/// <summary>Escritura de logs de integración (en scope propio, sobrevive al rollback del caso de uso).</summary>
public interface IIntegrationLogWriter
{
    Task WriteAsync(IntegrationLog log, CancellationToken ct = default);
}

/// <summary>Filtros de consulta de logs.</summary>
public sealed record LogQueryFilter(
    Guid? TenantId,
    string? LogType,
    Guid? CorrelationId,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize,
    /// <summary>
    /// Búsqueda de texto libre sobre la RUTA del log. Pensada para rastrear un trámite por su número
    /// (el TransactionFlit que recibe el cliente): las llamadas de estado/reproceso/cierre lo llevan en
    /// la ruta (p.ej. /api/v1/status-process/byId/82). No es un uuid, así que no rompe con valores como "82".
    /// </summary>
    string? Search = null);

public sealed record LogPage(IReadOnlyList<IntegrationLog> Items, int Total, int Page, int PageSize);

/// <summary>Lectura paginada de logs (para el submódulo frontend). Enmascara PII al servir.</summary>
public interface IIntegrationLogQuery
{
    Task<LogPage> QueryAsync(LogQueryFilter filter, CancellationToken ct = default);
}

/// <summary>Métricas de alerta ICT (para el panel del submódulo y para analytics.alert_rules).</summary>
public sealed record IctAlertMetrics(
    long StuckInValidation,
    double NoveltyRatePct,
    long WebhookDeliveryFailures,
    long JobsOutOfSla);

/// <summary>Cálculo de las métricas de alerta ICT sobre el schema ict.</summary>
public interface IIctAlertMetricsQuery
{
    Task<IctAlertMetrics> GetAsync(Guid? tenantId, CancellationToken ct = default);
}
