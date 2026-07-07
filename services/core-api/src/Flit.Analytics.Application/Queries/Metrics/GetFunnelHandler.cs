using Flit.Analytics.Application.Abstractions;

namespace Flit.Analytics.Application.Queries.Metrics;

/// <summary>
/// Consulta del funnel de estados (Reportes 2.0 §4.3): instancias distintas que alcanzaron cada
/// etapa, filtradas por <c>created_at</c> de la instancia en el rango.
/// </summary>
public sealed record GetFunnelQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    Guid? TransitOfficeId = null,
    Guid? ProcedureTypeId = null,
    Guid? OperatorUserId = null,
    string? Status = null,
    string? Reason = null,
    string? CompareWith = null);

/// <summary>
/// Compone el funnel operacional (<see cref="IAnalyticsMetricsReadRepository"/>) con los pasos del
/// wizard de la telemetría HU-A (<see cref="IUsageMetricsReadRepository"/>). Sin telemetría →
/// <c>wizardSteps</c> = lista vacía (no null, no error).
/// </summary>
public sealed class GetFunnelHandler(
    IAnalyticsMetricsReadRepository repo,
    IUsageMetricsReadRepository usageRepo)
{
    public async Task<(ComparedResponse<FunnelDto>? Result, string? Error)> HandleAsync(
        GetFunnelQuery query, CancellationToken ct = default)
    {
        if (query.From > query.To)
            return (null, "invalid_range");
        if (!MetricsQueryValidation.IsValidCompareWith(query.CompareWith))
            return (null, "invalid_compare_with");

        var filter = new MetricsFilter(
            query.TenantId, query.From, query.To,
            query.TransitOfficeId, query.ProcedureTypeId, query.OperatorUserId,
            query.Status, query.Reason);

        var current = await BuildAsync(filter, ct);

        var window = MetricsQueryValidation.ResolveComparisonWindow(query.CompareWith, query.From, query.To);
        if (window is not { } prev)
            return (new ComparedResponse<FunnelDto>(current, null, null), null);

        var previous = await BuildAsync(filter with { From = prev.From, To = prev.To }, ct);
        var comparison = new ComparisonInfoDto(query.CompareWith!, prev.From, prev.To);
        return (new ComparedResponse<FunnelDto>(current, previous, comparison), null);
    }

    private async Task<FunnelDto> BuildAsync(MetricsFilter filter, CancellationToken ct)
    {
        var core = await repo.GetFunnelAsync(filter, ct);
        var wizardSteps = await usageRepo.GetWizardStepMetricsAsync(
            filter.TenantId, filter.From, filter.To, ct);
        return new FunnelDto(
            core.States, core.Anulados, core.RechazadosVigentes, wizardSteps ?? []);
    }
}
