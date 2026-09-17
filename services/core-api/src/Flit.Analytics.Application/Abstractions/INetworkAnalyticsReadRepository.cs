using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Abstractions;

/// <summary>
/// HU #12359 (Feature #12257, épica #12235) — lectura analítica de la RED de una cabeza de grupo
/// (Concesión / Marca Blanca) sobre un conjunto explícito de clientes legibles. Es una abstracción
/// APARTE de <see cref="IAnalyticsReadRepository"/> a propósito (AC6/AC7): las firmas <c>Guid?</c>
/// existentes —con su semántica «<c>null</c> = todas las compañías»— no cambian ni reciben un
/// parámetro nuevo; la rama de red no tiene vista global.
/// <para>Contrato de la implementación (AC3/AC4):</para>
/// <list type="bullet">
///   <item>El conjunto viaja a la base como UN parámetro de arreglo (<c>uuid[]</c>,
///   <c>WHERE tenant_id = ANY(@tenants)</c>); jamás se interpola un identificador en el texto SQL.</item>
///   <item>Un conjunto vacío devuelve el resultado vacío SIN ejecutar la consulta: el vacío nunca se
///   traduce en «sin filtro».</item>
///   <item>Cada resultado lleva los clientes DISTINTOS del conjunto con datos en el agregado
///   (<see cref="NetworkAnalyticsResult{T}.ReachedTenantIds"/>) para la auditoría de acceso (HU #12361).</item>
/// </list>
/// </summary>
public interface INetworkAnalyticsReadRepository
{
    /// <summary>Conteos por categoría y estado del universo de <paramref name="tenantIds"/> en el rango.</summary>
    /// <remarks>BUG #12588 — fechas null = sin acotar; mismo criterio que el overview propio.</remarks>
    Task<NetworkAnalyticsResult<IReadOnlyList<CategoryMetricsDto>>> GetNetworkOverviewAsync(
        IReadOnlySet<Guid> tenantIds, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default);

    /// <summary>Top de radicadores de la red (ranking único por usuario, sumando sus radicaciones en cualquier cliente del conjunto).</summary>
    Task<NetworkAnalyticsResult<IReadOnlyList<TopProducerDto>>> GetNetworkTopProducersAsync(
        IReadOnlySet<Guid> tenantIds, DateOnly fromDate, DateOnly toDate, int limit, CancellationToken ct = default);

    /// <summary>Tendencia mensual por categoría del universo de <paramref name="tenantIds"/> en el rango.</summary>
    Task<NetworkAnalyticsResult<IReadOnlyList<MonthlyTrendPointDto>>> GetNetworkMonthlyTrendAsync(
        IReadOnlySet<Guid> tenantIds, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
}
