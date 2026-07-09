namespace Flit.Analytics.Application.Abstractions;

/// <summary>
/// Lectura agregada de <c>analytics.app_usage_events</c> (telemetría HU-A, Reportes 2.0).
/// Implementada en Flit.Infrastructure (HU-A); consumida por los handlers de métricas (HU-B).
/// Todas las consultas filtran por tenant (RLS + WHERE explícito).
/// </summary>
public interface IUsageMetricsReadRepository
{
    Task<IReadOnlyList<WizardStepMetricDto>> GetWizardStepMetricsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<ModuleUsageDto>> GetModuleUsageAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<PeakHourDto>> GetPeakHoursAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<WizardDurationDto> GetWizardDurationAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);
}
