using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #12358 (Feature #12257, épica #12235) — vista consolidada de la red de una cabeza de grupo
/// (Concesión / Marca Blanca). Rutas NUEVAS bajo el prefijo único <c>/api/v1/tramites/network</c>
/// (decisión 1 del plan): las rutas existentes no cambian ni reciben parámetros (AC6), el
/// <c>TenantEnforcementMiddleware</c> las cubre con una sola entrada <c>Prefix</c> (AC7) y la
/// superficie de lectura ancha se enumera con un <c>grep</c>.
/// <list type="bullet">
///   <item><c>GET /instances</c> y <c>POST /instances/search</c>: mismo contrato de filtros, orden y
///   paginación que <c>/instances</c> + filtro <c>childTenantId</c>; SIEMPRE paginado con tope en
///   servidor (AC10). Cada fila lleva el cliente dueño (<c>tenantId</c>, <c>companiaNombre</c>).</item>
///   <item><c>POST /instances/estado-counts</c>: tira de KPIs del universo consolidado.</item>
///   <item><c>GET /instances/{id}</c>: detalle en solo lectura, mismos campos que el detalle propio
///   más <c>tenantId</c>/<c>tenantName</c> del dueño. Sin <c>preview-url</c> ni contenido de
///   documentos (AC5): el único canal de contenido es la descarga proxeada de la HU #12410.</item>
///   <item><c>GET /stats/overview</c>, <c>GET /stats/productivity/top</c>, <c>GET /stats/monthly-trend</c>
///   (HU #12359, <see cref="NetworkAnalyticsEndpoints"/>): estadísticas agregadas del universo consolidado.</item>
///   <item><c>GET /reports/procedures</c> y <c>GET /reports/procedures/export</c> (HU #12360,
///   <see cref="NetworkReportsEndpoints"/>): reporte detallado de la red, filtrable por hijo o agregado.</item>
///   <item><c>GET /instances/{id}/attachments</c> y <c>GET /instances/{id}/attachments/{attachmentId}/download</c>
///   (HU #12410, <see cref="NetworkAttachmentEndpoints"/>): metadatos y descarga proxeada de los documentos
///   de un trámite de la red, con interruptor de clase para CONCESION.</item>
///   <item><c>GET /children</c> (HU #12555, <see cref="NetworkChildrenEndpoints"/>): clientes hijos
///   vigentes de la cabeza (id + nombre), sin la policy admin de <see cref="AdminCompanyChildrenEndpoints"/>.</item>
/// </list>
/// Policy de cabeza (<see cref="GroupHeadReadFilter"/>) sobre todo el grupo: sin alcance de grupo ⇒ 403.
/// HU #12361: <see cref="NetworkAccessAuditFilter"/> (más externo) escribe UN registro por petición en
/// <c>tramites.network_access_audit</c> con el desenlace que cada ruta publica vía
/// <see cref="NetworkAccessAuditContext.Publish"/> — hijos alcanzados, recurso y filtros sin PII.
/// El alcance sale de <see cref="RequestTenantResolver.ScopeFromItems"/> (BD vía middleware), jamás del
/// caller; <c>childTenantId</c> fuera del conjunto de lectura ⇒ 403 sin consulta.
/// </summary>
internal static class NetworkProcedureEndpoints
{
    internal const string RoutePrefix = "/api/v1/tramites/network";

    internal static IEndpointRouteBuilder MapTramitesNetworkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags("Trámites · Red (vista consolidada)")
            // HU #12361 — primero el auditor (envuelve a la policy): un 403 de la policy no deja rastro
            // porque el actor no es cabeza; un 403 por childTenantId ajeno sí (lo publica la ruta).
            .AddEndpointFilter<NetworkAccessAuditFilter>()
            .AddEndpointFilter<GroupHeadReadFilter>();

        // GET /network/instances — query equivalente al GET /instances. Sin query string NO hay camino
        // «histórico sin paginar»: siempre primera página con el tope del servidor (AC10).
        group.MapGet("/instances", async (
            HttpContext http,
            NetworkListProcedureInstancesHandler handler,
            [FromQuery] Guid? childTenantId,
            [FromQuery] string? vin,
            [FromQuery] string? placa,
            [FromQuery] string? vendedor,
            [FromQuery] string? comprador,
            [FromQuery] string? gestor,
            [FromQuery] bool? firmado,
            [FromQuery] string? estado,
            [FromQuery] string? modalidad,
            [FromQuery] string? organismoTransito,
            [FromQuery] string? tipoCodigo,
            [FromQuery] string? busqueda,
            [FromQuery] bool? prioritario,
            [FromQuery] DateTimeOffset? createdFrom,
            [FromQuery] DateTimeOffset? createdTo,
            [FromQuery] DateTimeOffset? updatedFrom,
            [FromQuery] DateTimeOffset? updatedTo,
            [FromQuery] string? sortBy,
            [FromQuery] string? sortDir,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            CancellationToken ct) =>
        {
            var request = new ProcedureInstanceListRequest
            {
                Skip = skip ?? 0,
                Take = take ?? ListProcedureInstancesHandler.MaxItems,
                Vin = vin,
                Placa = placa,
                Vendedor = vendedor,
                Comprador = comprador,
                Gestor = gestor,
                Firmado = firmado,
                Estados = ProcedureInstanceEndpoints.ParseEstados(estado),
                Modalidad = modalidad,
                OrganismoTransito = organismoTransito,
                TipoCodigo = tipoCodigo,
                Busqueda = busqueda,
                Prioritario = prioritario,
                CreatedFrom = createdFrom,
                CreatedTo = createdTo,
                UpdatedFrom = updatedFrom,
                UpdatedTo = updatedTo,
                SortBy = sortBy,
                SortDescending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase),
            };

            var (items, total, error) = await handler.HandleAsync(
                RequestTenantResolver.ScopeFromItems(http), childTenantId, request, ct);
            PublishListOutcome(http, NetworkAccessVocabulary.Resources.InstancesSearch, childTenantId, request, items.Select(i => i.TenantId), error);
            return error is not null ? Forbidden(error) : Results.Ok(new { items, total });
        })
            .WithName("NetworkListProcedureInstances")
            .WithSummary("Listado consolidado de trámites de la red (cabeza de grupo)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // POST /network/instances/search — mismo body que POST /instances/search + childTenantId.
        group.MapPost("/instances/search", async (
            HttpContext http,
            NetworkListProcedureInstancesHandler handler,
            [FromBody] NetworkTramitesSearchRequest body,
            CancellationToken ct) =>
        {
            if (TramitesQueryConditions.Validate(body.Condiciones) is { } problema)
                return Results.BadRequest(new { error = problema });

            var request = body.ToRequest(tenantId: null);
            var (items, total, error) = await handler.HandleAsync(
                RequestTenantResolver.ScopeFromItems(http), body.ChildTenantId, request, ct);
            PublishListOutcome(http, NetworkAccessVocabulary.Resources.InstancesSearch, body.ChildTenantId, request, items.Select(i => i.TenantId), error);
            return error is not null ? Forbidden(error) : Results.Ok(new { items, total });
        })
            .WithName("NetworkSearchProcedureInstances")
            .WithSummary("Listado consolidado de la red filtrado con la gramática de consultas")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // POST /network/instances/estado-counts — KPIs del universo consolidado bajo las mismas condiciones.
        group.MapPost("/instances/estado-counts", async (
            HttpContext http,
            NetworkCountProcedureInstancesByStatusHandler handler,
            [FromBody] NetworkTramitesSearchRequest body,
            CancellationToken ct) =>
        {
            if (TramitesQueryConditions.Validate(body.Condiciones) is { } problema)
                return Results.BadRequest(new { error = problema });

            var request = body.ToRequest(tenantId: null);
            var (result, error) = await handler.HandleWithReachAsync(
                RequestTenantResolver.ScopeFromItems(http), body.ChildTenantId, request, ct);
            PublishListOutcome(http, NetworkAccessVocabulary.Resources.StatsOverview, body.ChildTenantId, request, result?.ReachedTenantIds ?? [], error);
            return error is not null ? Forbidden(error) : Results.Ok(result!.Counts);
        })
            .WithName("NetworkProcedureInstanceEstadoCounts")
            .WithSummary("Conteo por estado del universo consolidado de la red")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /network/instances/{id} — detalle en solo lectura. 404 escueto si el id no está en el
        // conjunto de lectura (mismo mensaje que el detalle propio: no se distingue «ajeno» de «no existe»).
        group.MapGet("/instances/{id:guid}", async (
            Guid id,
            HttpContext http,
            NetworkGetProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, RequestTenantResolver.ScopeFromItems(http), ct);
            // AC2 — el detalle de un trámite de un hijo lleva el trámite y su dueño. Un 404 no publica
            // nada: no se sabe (ni se revela) de quién sería el trámite.
            if (result is not null)
            {
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    NetworkAccessVocabulary.Resources.InstancesDetail,
                    NetworkAccessVocabulary.Results.Ok,
                    [result.TenantId],
                    ProcedureId: id,
                    ProcedureTenantId: result.TenantId));
            }

            return error switch
            {
                null => Results.Ok(result),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                _ => Forbidden(error),
            };
        })
            .WithName("NetworkGetProcedureInstance")
            .WithSummary("Detalle consolidado (solo lectura) de un trámite de la red")
            .Produces<NetworkProcedureInstanceDetailDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // HU #12359 — estadísticas agregadas de la red (/stats/overview, /stats/productivity/top,
        // /stats/monthly-trend) sobre el MISMO grupo: heredan policy de cabeza y auditoría.
        group.MapNetworkStats();
        // HU #12360 — reporte detallado de la red (listado + exportación) bajo /reports/*.
        group.MapNetworkReports();
        // HU #12410 — documentos de un trámite de la red: metadatos + descarga proxeada (sin preview-url).
        group.MapNetworkAttachments();
        // HU #12555 — GET /children: clientes hijos vigentes de la cabeza (id + nombre), sin policy admin.
        group.MapNetworkChildren();

        return app;
    }

    private static IResult Forbidden(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// HU #12361 — desenlace de un listado / estadística para el auditor: con éxito, los clientes
    /// presentes en el resultado (el filtro descarta a la cabeza: si solo hay datos propios no se
    /// registra, AC5); con <c>childTenantId</c> fuera del alcance, el intento rechazado sobre ese hijo.
    /// Otros errores (sin alcance de grupo) no publican: el actor no es cabeza.
    /// </summary>
    private static void PublishListOutcome(
        HttpContext http,
        string resource,
        Guid? childTenantId,
        ProcedureInstanceListRequest request,
        IEnumerable<Guid> reachedTenantIds,
        string? error)
    {
        var filters = NetworkAccessAuditContext.FiltersJson(request, childTenantId);
        switch (error)
        {
            case null:
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    resource, NetworkAccessVocabulary.Results.Ok, reachedTenantIds.Distinct().ToArray(), filters));
                break;
            case NetworkScopePolicy.ChildOutOfScope when childTenantId is { } child:
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    resource, NetworkAccessVocabulary.Results.Forbidden, [child], filters));
                break;
        }
    }
}

/// <summary>
/// Cuerpo de las rutas <c>POST /network/instances/search</c> y <c>/network/instances/estado-counts</c>:
/// el mismo <see cref="TramitesSearchRequest"/> más el filtro por cliente hijo (AC10).
/// </summary>
internal sealed record NetworkTramitesSearchRequest : TramitesSearchRequest
{
    /// <summary>Cliente de la red al que acotar (puede ser la propia cabeza). Fuera del alcance ⇒ 403.</summary>
    public Guid? ChildTenantId { get; init; }
}
