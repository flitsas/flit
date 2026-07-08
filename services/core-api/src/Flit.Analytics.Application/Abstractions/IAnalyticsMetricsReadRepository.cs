using Flit.Analytics.Application.Queries.Metrics;

namespace Flit.Analytics.Application.Abstractions;

/// <summary>
/// Lectura agregada de las métricas de Reportes 2.0 (HU-B) sobre las tablas operacionales:
/// <c>tramites.procedure_instances</c> / <c>procedure_instance_status_history</c> /
/// <c>procedure_instance_attachments</c> / <c>procedure_instance_biometric_validations</c> y
/// <c>admin.ot_api_call_logs</c>. Implementada en Flit.Infrastructure con SQL crudo (patrón
/// <c>AnalyticsReadRepository</c>: GUC RLS + WHERE explícito <c>tenant_id = @tenant</c>).
/// Lo que proviene de <c>analytics.app_usage_events</c> (telemetría) NO va aquí: lo expone
/// <see cref="IUsageMetricsReadRepository"/> (HU-A).
/// </summary>
public interface IAnalyticsMetricsReadRepository
{
    /// <summary>Métricas OT (§4.2): rechazos, tiempos de decisión, reincidencia, ranking y atascados.</summary>
    Task<OtMetricsDto> GetOtMetricsAsync(MetricsFilter filter, CancellationToken ct = default);

    /// <summary>Funnel de estados (§4.3), sin la parte de telemetría (wizardSteps la agrega el handler).</summary>
    Task<FunnelCoreDto> GetFunnelAsync(MetricsFilter filter, CancellationToken ct = default);

    /// <summary>Reemplazos de documentos por tipo (§4.4, regla §0 de adjuntos).</summary>
    Task<IReadOnlyList<DocumentReplacementDto>> GetDocumentReplacementsAsync(
        MetricsFilter filter, CancellationToken ct = default);

    /// <summary>Integraciones externas agregadas por endpoint/dirección (§4.4).</summary>
    Task<IReadOnlyList<ExternalApiMetricDto>> GetExternalApiMetricsAsync(
        MetricsFilter filter, CancellationToken ct = default);

    /// <summary>Panorama en vivo (§4.5) en UNA sola ronda de queries (batch multi-resultset).</summary>
    Task<LiveOverviewDataDto> GetLiveOverviewAsync(
        Guid tenantId, int stuckDays, CancellationToken ct = default);
}
