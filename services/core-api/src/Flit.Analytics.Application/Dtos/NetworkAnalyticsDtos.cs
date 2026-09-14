namespace Flit.Analytics.Application.Dtos;

// HU #12359 (Feature #12257) — resultados de las estadísticas de red. Los agregados (<see cref="CategoryMetricsDto"/>,
// <see cref="TopProducerDto"/>, <see cref="MonthlyTrendPointDto"/>) son EXACTAMENTE los mismos tipos de las
// rutas actuales para que el frontend reutilice sus modelos; lo que se añade es el alcance efectivo y los
// clientes alcanzados.

/// <summary>
/// Agregado de la red + clientes DISTINTOS del conjunto consultado con datos en él
/// (<see cref="ReachedTenantIds"/>, insumo de la auditoría de acceso consolidado, HU #12361).
/// </summary>
public sealed record NetworkAnalyticsResult<T>(T Items, IReadOnlyList<Guid> ReachedTenantIds)
{
    /// <summary>Resultado vacío (conjunto de lectura vacío, AC4): nada consultado, nadie alcanzado.</summary>
    public static NetworkAnalyticsResult<T> Empty(T items) => new(items, []);
}

/// <summary>Alcance efectivo de una consulta de red, informativo para el cliente (los ids que sí se consultaron).</summary>
public sealed record NetworkScopeDto(IReadOnlyList<Guid> TenantIds);

/// <summary>
/// Respuesta de <c>GET /api/v1/tramites/network/stats/overview</c>: misma forma que
/// <see cref="AnalyticsOverviewDto"/> (<c>tenantId</c> = cliente cabeza o hijo acotado, <c>from</c>,
/// <c>to</c>, <c>categories</c>) más <see cref="Scope"/>.
/// </summary>
public sealed record NetworkAnalyticsOverviewDto(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CategoryMetricsDto> Categories,
    NetworkScopeDto Scope);

/// <summary>Respuesta de <c>/network/stats/productivity/top</c> y <c>/network/stats/monthly-trend</c>: <c>{ items, scope }</c>.</summary>
public sealed record NetworkAnalyticsItemsDto<T>(IReadOnlyList<T> Items, NetworkScopeDto Scope);

/// <summary>
/// Lo que un handler de estadísticas de red entrega al endpoint: la respuesta serializable y, aparte,
/// los clientes alcanzados para el filtro de auditoría (no viajan al cliente).
/// </summary>
public sealed record NetworkAnalyticsOutcome<TResponse>(TResponse Response, IReadOnlyList<Guid> ReachedTenantIds);
