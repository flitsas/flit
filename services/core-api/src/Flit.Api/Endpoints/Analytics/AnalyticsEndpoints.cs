using System.Security.Claims;
using Flit.Analytics.Application.Queries;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Endpoints del dashboard analítico (HU #10243, Feature #10139).
/// El tenant se resuelve del claim JWT <c>tenant_id</c> (AC1); el SuperAdmin puede
/// indicar <c>tenantId</c> por query para consultar otra compañía (AC2). Un Tenant Admin
/// que solicite un tenant distinto al de su token recibe 403.
/// </summary>
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .WithTags("Analytics · Dashboard");

        group.MapGet("/overview", GetOverviewAsync)
            .WithName("AnalyticsOverview")
            .WithSummary("Métricas por categoría/estado del tenant en un rango de fechas")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/productivity/top", GetTopProducersAsync)
            .WithName("AnalyticsTopProducers")
            .WithSummary("Top de radicadores por trámites enviados en un rango de fechas")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetOverviewAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        GetAnalyticsOverviewHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!TryResolveEffectiveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(
            new GetAnalyticsOverviewQuery(tenant, from, to), ct);

        return err is "invalid_range" ? InvalidRange() : Results.Ok(result);
    }

    private static async Task<IResult> GetTopProducersAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        GetTopProducersHandler handler,
        CancellationToken ct,
        [FromQuery] int? limit = null,
        [FromQuery] Guid? tenantId = null)
    {
        if (!TryResolveEffectiveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (items, err) = await handler.HandleAsync(
            new GetTopProducersQuery(tenant, from, to, limit ?? GetTopProducersHandler.DefaultLimit), ct);

        return err is "invalid_range" ? InvalidRange() : Results.Ok(new { items });
    }

    /// <summary>
    /// AC1: usa el tenant del token. AC2: el SuperAdmin puede fijar otro tenant por query.
    /// Tenant Admin que pida un tenant ajeno → 403; ausencia total de tenant → 400.
    /// </summary>
    private static bool TryResolveEffectiveTenant(
        ClaimsPrincipal user, Guid? tenantIdQuery, out Guid tenant, out IResult? error)
    {
        tenant = Guid.Empty;
        error = null;

        var isSuperAdmin = user.IsInRole(AdminAuthorization.SuperAdminRole);
        var hasClaim = TryResolveTenantId(user, out var claimTenant);

        if (tenantIdQuery is { } requested && requested != Guid.Empty)
        {
            if (isSuperAdmin)
            {
                tenant = requested;
                return true;
            }

            if (hasClaim && requested == claimTenant)
            {
                tenant = claimTenant;
                return true;
            }

            error = Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "No está autorizado para consultar métricas de otro tenant.");
            return false;
        }

        if (hasClaim)
        {
            tenant = claimTenant;
            return true;
        }

        error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "Falta el tenant: el token no incluye tenant_id y no se indicó tenantId.");
        return false;
    }

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    private static IResult InvalidRange() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El rango de fechas es inválido: 'from' no puede ser posterior a 'to'.");
}
