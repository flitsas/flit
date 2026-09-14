using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Queries.Domain.Tenancy;

namespace Flit.Analytics.Application.Queries.Network;

/// <summary>
/// HU #12359 (Feature #12257, épica #12235) — regla común de las tres estadísticas de red:
/// <list type="number">
///   <item>El alcance sale del servidor (<see cref="TenantScope"/> del middleware), nunca de la petición
///   (AC5). Sin alcance de grupo ⇒ <see cref="NetworkScopePolicy.ScopeRequired"/>.</item>
///   <item><paramref name="childTenantId"/> acota a UN cliente del conjunto de lectura; uno ajeno ⇒
///   <see cref="NetworkScopePolicy.ChildOutOfScope"/> SIN ejecutar la consulta (AC5).</item>
///   <item>Rango inválido ⇒ <c>invalid_range</c> (mismo contrato que las rutas actuales).</item>
/// </list>
/// El conjunto efectivo (<see cref="TenantScope.ReadTenantIds"/>) es siempre un subconjunto del
/// alcance resuelto; el repositorio lo recibe como conjunto explícito y lo envía como arreglo (AC3).
/// </summary>
public static class NetworkAnalyticsScope
{
    public const string InvalidRange = "invalid_range";

    /// <summary>Conjunto efectivo de clientes a consultar o el código de error.</summary>
    public static (IReadOnlySet<Guid>? TenantIds, string? Error) Resolve(
        TenantScope? scope, Guid? childTenantId, DateOnly from, DateOnly to)
    {
        if (NetworkScopePolicy.Validate(scope) is { } scopeError)
            return (null, scopeError);

        var (effective, narrowError) = NetworkScopePolicy.Narrow(scope!, childTenantId);
        if (narrowError is not null)
            return (null, narrowError);

        if (from > to)
            return (null, InvalidRange);

        return (effective!.ReadTenantIds, null);
    }

    internal static NetworkScopeDto ToDto(IReadOnlySet<Guid> tenantIds) =>
        new(tenantIds.OrderBy(t => t).ToList());
}

/// <summary>Consulta del overview consolidado de la red (AC1).</summary>
public sealed record GetNetworkAnalyticsOverviewQuery(TenantScope? Scope, Guid? ChildTenantId, DateOnly From, DateOnly To);

/// <summary>
/// Overview de la red: misma forma que <c>GetAnalyticsOverviewHandler</c> sobre el conjunto de lectura.
/// <c>tenantId</c> de la respuesta = cliente cabeza (o el hijo acotado): nunca el centinela global.
/// </summary>
public sealed class GetNetworkAnalyticsOverviewHandler(INetworkAnalyticsReadRepository repo)
{
    public async Task<(NetworkAnalyticsOutcome<NetworkAnalyticsOverviewDto>? Result, string? Error)> HandleAsync(
        GetNetworkAnalyticsOverviewQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (tenantIds, error) = NetworkAnalyticsScope.Resolve(query.Scope, query.ChildTenantId, query.From, query.To);
        if (error is not null)
            return (null, error);

        var result = await repo.GetNetworkOverviewAsync(tenantIds!, query.From, query.To, ct).ConfigureAwait(false);
        var tenantId = query.ChildTenantId is { } child && child != Guid.Empty ? child : query.Scope!.WriteTenantId!.Value;
        var dto = new NetworkAnalyticsOverviewDto(tenantId, query.From, query.To, result.Items, NetworkAnalyticsScope.ToDto(tenantIds!));
        return (new NetworkAnalyticsOutcome<NetworkAnalyticsOverviewDto>(dto, result.ReachedTenantIds), null);
    }
}

/// <summary>Consulta del Top de productividad de la red (AC1).</summary>
public sealed record GetNetworkTopProducersQuery(TenantScope? Scope, Guid? ChildTenantId, DateOnly From, DateOnly To, int Limit);

/// <summary>Top de radicadores de la red; mismo límite por defecto y tope que <c>GetTopProducersHandler</c>.</summary>
public sealed class GetNetworkTopProducersHandler(INetworkAnalyticsReadRepository repo)
{
    public async Task<(NetworkAnalyticsOutcome<NetworkAnalyticsItemsDto<TopProducerDto>>? Result, string? Error)> HandleAsync(
        GetNetworkTopProducersQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (tenantIds, error) = NetworkAnalyticsScope.Resolve(query.Scope, query.ChildTenantId, query.From, query.To);
        if (error is not null)
            return (null, error);

        var limit = query.Limit <= 0 ? GetTopProducersHandler.DefaultLimit : Math.Min(query.Limit, GetTopProducersHandler.MaxLimit);
        var result = await repo.GetNetworkTopProducersAsync(tenantIds!, query.From, query.To, limit, ct).ConfigureAwait(false);
        var dto = new NetworkAnalyticsItemsDto<TopProducerDto>(result.Items, NetworkAnalyticsScope.ToDto(tenantIds!));
        return (new NetworkAnalyticsOutcome<NetworkAnalyticsItemsDto<TopProducerDto>>(dto, result.ReachedTenantIds), null);
    }
}

/// <summary>Consulta de la tendencia mensual de la red (AC1).</summary>
public sealed record GetNetworkMonthlyTrendQuery(TenantScope? Scope, Guid? ChildTenantId, DateOnly From, DateOnly To);

/// <summary>Tendencia mensual por categoría de la red (un punto por año/mes/categoría, sumando los clientes del conjunto).</summary>
public sealed class GetNetworkMonthlyTrendHandler(INetworkAnalyticsReadRepository repo)
{
    public async Task<(NetworkAnalyticsOutcome<NetworkAnalyticsItemsDto<MonthlyTrendPointDto>>? Result, string? Error)> HandleAsync(
        GetNetworkMonthlyTrendQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (tenantIds, error) = NetworkAnalyticsScope.Resolve(query.Scope, query.ChildTenantId, query.From, query.To);
        if (error is not null)
            return (null, error);

        var result = await repo.GetNetworkMonthlyTrendAsync(tenantIds!, query.From, query.To, ct).ConfigureAwait(false);
        var dto = new NetworkAnalyticsItemsDto<MonthlyTrendPointDto>(result.Items, NetworkAnalyticsScope.ToDto(tenantIds!));
        return (new NetworkAnalyticsOutcome<NetworkAnalyticsItemsDto<MonthlyTrendPointDto>>(dto, result.ReachedTenantIds), null);
    }
}
