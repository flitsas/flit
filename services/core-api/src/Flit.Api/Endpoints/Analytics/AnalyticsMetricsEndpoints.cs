using System.Security.Claims;
using Flit.Analytics.Application.Queries.Metrics;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Endpoints de métricas de Reportes 2.0 — HU-B (§4 del contrato): <c>ot-metrics</c>,
/// <c>funnel</c>, <c>usage</c> y <c>live-overview</c>. Mismas convenciones que
/// <see cref="AnalyticsEndpoints"/> (lecturas abiertas a cualquier autenticado del tenant), con
/// una diferencia deliberada: NO hay vista global — el SuperAdmin DEBE indicar <c>tenantId</c>
/// por query (sin tenant → 400 en español). El helper de resolución de tenant se replica aquí
/// (no se toca <c>AnalyticsEndpoints.cs</c>, propiedad §2 del contrato).
/// </summary>
public static class AnalyticsMetricsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics")
            .RequireAuthorization()
            .WithTags("Analytics · Métricas (Reportes 2.0)");

        group.MapGet("/ot-metrics", GetOtMetricsAsync)
            .WithName("AnalyticsOtMetrics")
            .WithSummary("Métricas del Organismo de Tránsito: rechazos, tiempos de decisión, reincidencia, ranking y atascados")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/funnel", GetFunnelAsync)
            .WithName("AnalyticsFunnel")
            .WithSummary("Funnel de estados del trámite (instancias que alcanzaron cada etapa) y pasos del wizard")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/usage", GetUsageAsync)
            .WithName("AnalyticsUsage")
            .WithSummary("Uso del aplicativo: módulos, wizard, horas pico, reemplazos de documentos e integraciones externas")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/live-overview", GetLiveOverviewAsync)
            .WithName("AnalyticsLiveOverview")
            .WithSummary("Panorama en vivo del tenant: actividad de hoy, atascados, validaciones pendientes e integraciones")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetOtMetricsAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        GetOtMetricsHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] Guid? operatorUserId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? reason = null,
        [FromQuery] string? compareWith = null,
        [FromQuery] int? stuckDays = null)
    {
        if (!TryResolveRequiredTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(
            new GetOtMetricsQuery(
                tenant, from, to, transitOfficeId, procedureTypeId, operatorUserId,
                status, reason, compareWith, stuckDays),
            ct);

        return err is not null ? MapError(err) : Results.Ok(result);
    }

    private static async Task<IResult> GetFunnelAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        GetFunnelHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] Guid? operatorUserId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? reason = null,
        [FromQuery] string? compareWith = null)
    {
        if (!TryResolveRequiredTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(
            new GetFunnelQuery(
                tenant, from, to, transitOfficeId, procedureTypeId, operatorUserId,
                status, reason, compareWith),
            ct);

        return err is not null ? MapError(err) : Results.Ok(result);
    }

    private static async Task<IResult> GetUsageAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        GetUsageHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] Guid? operatorUserId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? reason = null,
        [FromQuery] string? compareWith = null)
    {
        if (!TryResolveRequiredTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(
            new GetUsageQuery(
                tenant, from, to, transitOfficeId, procedureTypeId, operatorUserId,
                status, reason, compareWith),
            ct);

        return err is not null ? MapError(err) : Results.Ok(result);
    }

    private static async Task<IResult> GetLiveOverviewAsync(
        HttpContext httpContext,
        GetLiveOverviewHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] int? stuckDays = null)
    {
        if (!TryResolveRequiredTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(new GetLiveOverviewQuery(tenant, stuckDays), ct);

        return err is not null ? MapError(err) : Results.Ok(result);
    }

    /// <summary>
    /// Resolución de tenant de los endpoints de métricas (réplica del helper de
    /// <see cref="AnalyticsEndpoints"/> SIN vista global): usa el claim <c>tenant_id</c>; el
    /// SuperAdmin puede (y DEBE) indicar <c>tenantId</c> por query — sin tenant efectivo → 400.
    /// Tenant Admin que pida un tenant ajeno → 403.
    /// </summary>
    private static bool TryResolveRequiredTenant(
        ClaimsPrincipal user, Guid? tenantIdQuery, out Guid tenant, out IResult? error)
    {
        tenant = Guid.Empty;
        error = null;

        var isSuperAdmin = user.IsInRole(AdminAuthorization.SuperAdminRole);

        if (tenantIdQuery is { } requested && requested != Guid.Empty)
        {
            // Tenant explícito: SuperAdmin puede acceder a cualquiera; otros solo al propio.
            if (isSuperAdmin) { tenant = requested; return true; }

            var hasClaim = TryResolveTenantId(user, out var claimTenant);
            if (hasClaim && requested == claimTenant) { tenant = claimTenant; return true; }

            error = Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "No está autorizado para consultar métricas de otro tenant.");
            return false;
        }

        // SuperAdmin sin tenantId: NO hay vista global en los endpoints de métricas → 400.
        if (isSuperAdmin)
        {
            error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Este endpoint requiere especificar un tenantId: el SuperAdmin debe indicar la compañía.");
            return false;
        }

        // Usuario normal → usa el tenant del JWT.
        if (TryResolveTenantId(user, out var userTenant)) { tenant = userTenant; return true; }

        error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "Falta el tenant: el token no incluye tenant_id y no se indicó tenantId.");
        return false;
    }

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    private static IResult MapError(string error) => error switch
    {
        "invalid_range" => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El rango de fechas es inválido: 'from' no puede ser posterior a 'to'."),
        "invalid_compare_with" => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El parámetro 'compareWith' es inválido: use 'previous_period' o 'previous_year'."),
        "invalid_stuck_days" => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El parámetro 'stuckDays' es inválido: debe estar entre 1 y 90."),
        _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "Solicitud inválida."),
    };
}
