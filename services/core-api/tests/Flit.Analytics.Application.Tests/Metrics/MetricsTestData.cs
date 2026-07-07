using Flit.Analytics.Application.Queries.Metrics;

namespace Flit.Analytics.Application.Tests.Metrics;

/// <summary>Fixtures compartidos de los tests de métricas Reportes 2.0 (HU-B).</summary>
internal static class MetricsTestData
{
    public static OtMetricsDto OtMetrics() => new(
        new OtMetricsSummaryDto(120, 90, 18, 16.7, 52.4, 41.0, 130.0, 61.1, 7),
        [],
        [],
        [],
        [],
        [],
        new ReincidenceDto(18, 11, 1.4, 3),
        new StuckDto(7, []));

    public static FunnelCoreDto FunnelCore() => new(
        [
            new FunnelStageDto("borrador", 200, 100.0, 100.0),
            new FunnelStageDto("preparado", 150, 75.0, 75.0),
            new FunnelStageDto("entregado", 120, 60.0, 80.0),
            new FunnelStageDto("aprobado", 90, 45.0, 75.0),
        ],
        Anulados: 12,
        RechazadosVigentes: 18);

    public static LiveOverviewDataDto LiveOverview() => new(
        new LiveTodayDto(14, [new StatusCountItemDto("borrador", 6)], 5, 3, 1),
        StuckCount: 7,
        PendingIdentityValidations: 3,
        IntegrationsLastHour: new IntegrationsLastHourDto(25, 1, 350.0),
        LastActivityAt: DateTimeOffset.Parse("2026-07-07T13:59:01Z"));
}
