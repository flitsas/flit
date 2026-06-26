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

    /// <summary>
    /// Detalle paginado de trámites del tenant en el rango, filtrable por categoría y estado.
    /// <paramref name="category"/>/<paramref name="status"/> nulos o vacíos = sin filtro.
    /// Devuelve la página solicitada y el total del universo filtrado (HU #10244).
    /// </summary>
    Task<ProcedureDetailsPageDto> GetProcedureDetailsAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        string? category, string? status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Recorre en streaming el detalle de trámites filtrado (sin paginar), invocando
    /// <paramref name="onRowAsync"/> por cada fila a medida que llega del lector. Memoria
    /// acotada (no materializa el conjunto completo) para exports grandes (HU #10245).
    /// </summary>
    Task ExportProcedureDetailsAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, string? category, string? status,
        Func<ProcedureDetailDto, CancellationToken, Task> onRowAsync, CancellationToken ct = default);
}
