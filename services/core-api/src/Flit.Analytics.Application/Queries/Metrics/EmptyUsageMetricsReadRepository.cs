using Flit.Analytics.Application.Abstractions;

namespace Flit.Analytics.Application.Queries.Metrics;

/// <summary>
/// Fallback SIN datos de <see cref="IUsageMetricsReadRepository"/> (Reportes 2.0 HU-B). La
/// implementación real sobre <c>analytics.app_usage_events</c> la entrega HU-A en
/// Flit.Infrastructure; este fallback se registra con <c>TryAddScoped</c> para que el host (y los
/// tests de integración) arranquen aunque la telemetría aún no esté integrada: los endpoints de
/// métricas responden con listas vacías (§4.3/§4.4: "[] si no hay telemetría"). Cuando HU-A
/// registre la implementación real, ésta prevalece (TryAdd no pisa un registro existente y un
/// AddScoped posterior gana en la resolución).
/// </summary>
internal sealed class EmptyUsageMetricsReadRepository : IUsageMetricsReadRepository
{
    public Task<IReadOnlyList<WizardStepMetricDto>> GetWizardStepMetricsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WizardStepMetricDto>>([]);

    public Task<IReadOnlyList<ModuleUsageDto>> GetModuleUsageAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ModuleUsageDto>>([]);

    public Task<IReadOnlyList<PeakHourDto>> GetPeakHoursAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PeakHourDto>>([]);

    public Task<WizardDurationDto> GetWizardDurationAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        Task.FromResult(new WizardDurationDto(null, null));
}
