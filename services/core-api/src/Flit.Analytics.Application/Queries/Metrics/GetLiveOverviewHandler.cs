using Flit.Analytics.Application.Abstractions;

namespace Flit.Analytics.Application.Queries.Metrics;

/// <summary>
/// Panorama en vivo del tenant (Reportes 2.0 §4.5). SIN <c>compareWith</c> ni rango de fechas:
/// "hoy" es el día calendario America/Bogota. <see cref="StuckDays"/> default 7 (1..90).
/// </summary>
public sealed record GetLiveOverviewQuery(Guid TenantId, int? StuckDays = null);

/// <summary>
/// Valida <c>stuckDays</c>, delega en el repositorio (una sola ronda de queries, objetivo
/// &lt; 300 ms) y estampa <c>generatedAt</c>.
/// </summary>
public sealed class GetLiveOverviewHandler(IAnalyticsMetricsReadRepository repo)
{
    public async Task<(LiveOverviewDto? Result, string? Error)> HandleAsync(
        GetLiveOverviewQuery query, CancellationToken ct = default)
    {
        if (!MetricsQueryValidation.IsValidStuckDays(query.StuckDays))
            return (null, "invalid_stuck_days");

        var stuckDays = query.StuckDays ?? MetricsQueryValidation.DefaultStuckDays;
        var data = await repo.GetLiveOverviewAsync(query.TenantId, stuckDays, ct);

        return (new LiveOverviewDto(
            DateTimeOffset.UtcNow,
            data.Today,
            data.StuckCount,
            data.PendingIdentityValidations,
            data.IntegrationsLastHour,
            data.LastActivityAt), null);
    }
}
