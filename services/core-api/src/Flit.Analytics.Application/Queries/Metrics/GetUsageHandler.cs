using Flit.Analytics.Application.Abstractions;

namespace Flit.Analytics.Application.Queries.Metrics;

/// <summary>Consulta de métricas de uso del aplicativo (Reportes 2.0 §4.4).</summary>
public sealed record GetUsageQuery(
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
/// Compone las métricas de uso: <c>moduleUsage/wizardSteps/peakHours</c> y duración del wizard
/// desde la telemetría HU-A (<see cref="IUsageMetricsReadRepository"/>);
/// <c>documentReplacements/externalApis</c> desde las tablas operacionales
/// (<see cref="IAnalyticsMetricsReadRepository"/>). Sin datos → listas vacías (no null, no error)
/// y duraciones null.
/// </summary>
public sealed class GetUsageHandler(
    IAnalyticsMetricsReadRepository repo,
    IUsageMetricsReadRepository usageRepo)
{
    public async Task<(ComparedResponse<UsageDto>? Result, string? Error)> HandleAsync(
        GetUsageQuery query, CancellationToken ct = default)
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
            return (new ComparedResponse<UsageDto>(current, null, null), null);

        var previous = await BuildAsync(filter with { From = prev.From, To = prev.To }, ct);
        var comparison = new ComparisonInfoDto(query.CompareWith!, prev.From, prev.To);
        return (new ComparedResponse<UsageDto>(current, previous, comparison), null);
    }

    private async Task<UsageDto> BuildAsync(MetricsFilter filter, CancellationToken ct)
    {
        var moduleUsage = await usageRepo.GetModuleUsageAsync(filter.TenantId, filter.From, filter.To, ct);
        var wizardSteps = await usageRepo.GetWizardStepMetricsAsync(filter.TenantId, filter.From, filter.To, ct);
        var peakHours = await usageRepo.GetPeakHoursAsync(filter.TenantId, filter.From, filter.To, ct);
        var wizardDuration = await usageRepo.GetWizardDurationAsync(filter.TenantId, filter.From, filter.To, ct);
        var documentReplacements = await repo.GetDocumentReplacementsAsync(filter, ct);
        var externalApis = await repo.GetExternalApiMetricsAsync(filter, ct);

        return new UsageDto(
            moduleUsage ?? [],
            wizardSteps ?? [],
            peakHours ?? [],
            documentReplacements ?? [],
            externalApis ?? [],
            wizardDuration?.AvgDurationMs,
            wizardDuration?.MedianDurationMs);
    }
}
