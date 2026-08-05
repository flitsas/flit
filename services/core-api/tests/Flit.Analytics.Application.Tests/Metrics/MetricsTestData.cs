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
        new StuckDto(7, []),
        // Causales tipificadas: dos causales sobre 18 rechazos. Los porcentajes NO suman 100 % a
        // propósito — un rechazo puede llevar varias, y el fixture lo refleja (66,7 + 38,9).
        [
            new RejectionByReasonCatalogDto(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "soat_no_vigente", "SOAT no vigente", 12, 66.7),
            new RejectionByReasonCatalogDto(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "impuestos_no_vigentes", "Impuestos no vigentes", 7, 38.9),
        ],
        1.06,
        new InternalCycleDto(38.5, 26.0, 96.0));

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
