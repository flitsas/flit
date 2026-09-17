using System.Text.Json;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Network;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #12359 (Feature #12257, épica #12235) — estadísticas agregadas de la red de una cabeza de grupo.
/// Rutas NUEVAS bajo el grupo <c>/api/v1/tramites/network</c> (AC7: las rutas de <c>/api/v1/analytics</c>
/// no cambian ni reciben parámetros nuevos; el prefijo ya está en <c>RuntimeScopedRoutes</c> y hereda
/// <c>GroupHeadReadFilter</c> + <c>NetworkAccessAuditFilter</c> del grupo):
/// <list type="bullet">
///   <item><c>GET /stats/overview</c> — mismo contrato que <c>/analytics/overview</c> (+ <c>scope</c>).</item>
///   <item><c>GET /stats/productivity/top</c> — mismo contrato que <c>/analytics/productivity/top</c> (+ <c>scope</c>).</item>
///   <item><c>GET /stats/monthly-trend</c> — mismo contrato que <c>/analytics/monthly-trend</c> (+ <c>scope</c>).</item>
/// </list>
/// Solo se amplían las consultas que el Dashboard consume hoy (<c>fetchAnalyticsOverview</c>,
/// <c>fetchMonthlyTrend</c>) más el Top de productividad (pestaña Productividad de Reportes); las
/// métricas de Reportes 2.0 (<c>ot-metrics</c>, <c>funnel</c>, <c>usage</c>, <c>live-overview</c>) no las
/// consume el Dashboard y quedan fuera.
/// <para>
/// Query: <c>from</c>, <c>to</c> (obligatorios), <c>childTenantId</c> opcional (subconjunto del alcance;
/// ajeno ⇒ 403 <c>network_child_out_of_scope</c> SIN consulta, AC5) y <c>limit</c> en el Top. El
/// alcance nunca sale de la petición: <see cref="RequestTenantResolver.ScopeFromItems"/>.
/// </para>
/// <para>
/// Auditoría (HU #12361): <c>reached_tenant_ids</c> = hijos con datos en el agregado (opción «hijos con
/// datos en el resultado»: cada consulta de red proyecta <c>tenant_id</c> para saberlo sin una segunda consulta).
/// </para>
/// </summary>
internal static class NetworkAnalyticsEndpoints
{
    private static readonly JsonSerializerOptions FiltersJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    internal static RouteGroupBuilder MapNetworkStats(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // BUG #12588 — `from`/`to` opcionales: omitirlos consolida el universo completo de la red.
        // Mismo criterio que `/analytics/overview`; el resto de rutas de stats mantiene el rango
        // obligatorio porque son series y rankings, que sin ventana no significan nada.
        group.MapGet("/stats/overview", async (
            HttpContext http,
            GetNetworkAnalyticsOverviewHandler handler,
            CancellationToken ct,
            [FromQuery] DateOnly? from = null,
            [FromQuery] DateOnly? to = null,
            [FromQuery] Guid? childTenantId = null) =>
        {
            var (result, error) = await handler.HandleAsync(
                new GetNetworkAnalyticsOverviewQuery(RequestTenantResolver.ScopeFromItems(http), childTenantId, from, to), ct);
            PublishOutcome(http, NetworkAccessVocabulary.Resources.StatsOverview, childTenantId, from, to, null, result?.ReachedTenantIds, error);
            return error is not null ? MapError(error) : Results.Ok(result!.Response);
        })
            .WithName("NetworkStatsOverview")
            .WithSummary("Métricas por categoría/estado del universo consolidado de la red")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/stats/productivity/top", async (
            HttpContext http,
            DateOnly from,
            DateOnly to,
            GetNetworkTopProducersHandler handler,
            CancellationToken ct,
            [FromQuery] int? limit = null,
            [FromQuery] Guid? childTenantId = null) =>
        {
            var (result, error) = await handler.HandleAsync(
                new GetNetworkTopProducersQuery(
                    RequestTenantResolver.ScopeFromItems(http), childTenantId, from, to, limit ?? GetTopProducersHandler.DefaultLimit),
                ct);
            PublishOutcome(http, NetworkAccessVocabulary.Resources.StatsProductivityTop, childTenantId, from, to, limit, result?.ReachedTenantIds, error);
            return error is not null ? MapError(error) : Results.Ok(result!.Response);
        })
            .WithName("NetworkStatsTopProducers")
            .WithSummary("Top de radicadores de la red por trámites enviados")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/stats/monthly-trend", async (
            HttpContext http,
            DateOnly from,
            DateOnly to,
            GetNetworkMonthlyTrendHandler handler,
            CancellationToken ct,
            [FromQuery] Guid? childTenantId = null) =>
        {
            var (result, error) = await handler.HandleAsync(
                new GetNetworkMonthlyTrendQuery(RequestTenantResolver.ScopeFromItems(http), childTenantId, from, to), ct);
            PublishOutcome(http, NetworkAccessVocabulary.Resources.StatsMonthlyTrend, childTenantId, from, to, null, result?.ReachedTenantIds, error);
            return error is not null ? MapError(error) : Results.Ok(result!.Response);
        })
            .WithName("NetworkStatsMonthlyTrend")
            .WithSummary("Tendencia mensual de trámites por categoría del universo consolidado de la red")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return group;
    }

    private static IResult MapError(string error) => error switch
    {
        NetworkAnalyticsScope.InvalidRange => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El rango de fechas es inválido: 'from' no puede ser posterior a 'to'."),
        _ => Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden),
    };

    /// <summary>
    /// Desenlace para el auditor (HU #12361): con éxito, los hijos con datos en el agregado; con
    /// <c>childTenantId</c> fuera del alcance, el intento rechazado sobre ese hijo. Sin alcance de grupo
    /// o rango inválido no se publica nada. Filtros: solo fechas, límite y cliente (sin PII).
    /// </summary>
    private static void PublishOutcome(
        HttpContext http,
        string resource,
        Guid? childTenantId,
        DateOnly? from,
        DateOnly? to,
        int? limit,
        IReadOnlyList<Guid>? reachedTenantIds,
        string? error)
    {
        var filters = JsonSerializer.Serialize(new { childTenantId, from, to, limit }, FiltersJsonOptions);
        switch (error)
        {
            case null:
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    resource, NetworkAccessVocabulary.Results.Ok, reachedTenantIds ?? [], filters));
                break;
            case NetworkScopePolicy.ChildOutOfScope when childTenantId is { } child:
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    resource, NetworkAccessVocabulary.Results.Forbidden, [child], filters));
                break;
        }
    }
}
