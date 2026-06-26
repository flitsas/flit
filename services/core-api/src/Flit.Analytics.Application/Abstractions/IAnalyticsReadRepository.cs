using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Abstractions;

/// <summary>
/// Acceso de solo lectura a los agregados analíticos (schema <c>analytics</c>, HU #10153/#10240).
/// La implementación fija <c>app.current_tenant_id</c> para respetar RLS antes de consultar.
/// </summary>
public interface IAnalyticsReadRepository
{
    /// <summary>
    /// Conteos por categoría (matriculas/traspasos/otros) y estado para el tenant y rango dados.
    /// </summary>
    Task<IReadOnlyList<CategoryMetricsDto>> GetOverviewAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>
    /// Top de radicadores ordenados por trámites enviados (submitted) descendente, hasta <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<TopProducerDto>> GetTopProducersAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, int limit, CancellationToken ct = default);
}
