using Flit.Analytics.Application.Abstractions;

namespace Flit.Analytics.Application.Queries.Metrics;

/// <summary>
/// Consulta de métricas del Organismo de Tránsito (Reportes 2.0 §4.2). El tenant SIEMPRE viene
/// resuelto por el endpoint (no hay vista global). <see cref="CompareWith"/> opcional
/// (<c>previous_period</c> | <c>previous_year</c>); <see cref="StuckDays"/> default 7 (1..90).
/// </summary>
public sealed record GetOtMetricsQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    Guid? TransitOfficeId = null,
    Guid? ProcedureTypeId = null,
    Guid? OperatorUserId = null,
    string? Status = null,
    string? Reason = null,
    string? CompareWith = null,
    int? StuckDays = null);

/// <summary>
/// Valida rango/compareWith/stuckDays, consulta la ventana actual (y la de comparación, si se
/// pidió) y envuelve <c>{current, previous, comparison}</c> (§4.1). El backend NO calcula deltas.
/// </summary>
public sealed class GetOtMetricsHandler(IAnalyticsMetricsReadRepository repo)
{
    public async Task<(ComparedResponse<OtMetricsDto>? Result, string? Error)> HandleAsync(
        GetOtMetricsQuery query, CancellationToken ct = default)
    {
        if (query.From > query.To)
            return (null, "invalid_range");
        if (!MetricsQueryValidation.IsValidStuckDays(query.StuckDays))
            return (null, "invalid_stuck_days");
        if (!MetricsQueryValidation.IsValidCompareWith(query.CompareWith))
            return (null, "invalid_compare_with");

        var stuckDays = query.StuckDays ?? MetricsQueryValidation.DefaultStuckDays;
        var filter = new MetricsFilter(
            query.TenantId, query.From, query.To,
            query.TransitOfficeId, query.ProcedureTypeId, query.OperatorUserId,
            query.Status, query.Reason, stuckDays);

        var current = await repo.GetOtMetricsAsync(filter, ct);

        var window = MetricsQueryValidation.ResolveComparisonWindow(query.CompareWith, query.From, query.To);
        if (window is not { } prev)
            return (new ComparedResponse<OtMetricsDto>(current, null, null), null);

        var previous = await repo.GetOtMetricsAsync(
            filter with { From = prev.From, To = prev.To }, ct);
        var comparison = new ComparisonInfoDto(query.CompareWith!, prev.From, prev.To);
        return (new ComparedResponse<OtMetricsDto>(current, previous, comparison), null);
    }
}
