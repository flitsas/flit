using System.Security.Claims;
using Flit.Admin.Application.Companies.Settings.GetActiveModules;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Módulos activos del dashboard para el usuario autenticado (HU #12251, Feature #12249).
/// A diferencia de GET /api/v1/admin/companies/{tenantId}/settings
/// (<see cref="AdminAuthorization.AdminCompanyPolicy"/>), este endpoint solo exige
/// autenticación (<c>RequireAuthorization()</c>, sin policy): cualquier rol de la compañía
/// necesita saber qué secciones del dashboard mostrar, no solo Admin/SuperAdmin.
/// Mismo modelo de resolución de tenant que las lecturas de <see cref="AnalyticsEndpoints"/>
/// (claim JWT <c>tenant_id</c>; SuperAdmin con <c>tenantId</c> explícito por query), con la
/// diferencia de que aquí NUNCA hay vista global: sin tenant concreto no hay flags de UNA
/// compañía que devolver, así que el SuperAdmin sin tenantId recibe 400 — mismo criterio que
/// ya aplican <c>ExportExcel</c>/<c>GetProcedureDetailsAsync</c>/<c>ExportExecutivePdfAsync</c>
/// dentro de <see cref="AnalyticsEndpoints"/>.
/// </summary>
public static class DashboardActiveModulesEndpoints
{
    public static IEndpointRouteBuilder MapDashboardActiveModulesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics")
            .RequireAuthorization()
            .WithTags("Analytics · Dashboard");

        group.MapGet("/active-modules", GetActiveModulesAsync)
            .WithName("AnalyticsActiveModules")
            .WithSummary("Flags de módulos activos del dashboard (Trámites/Comparendos/Resoluciones)")
            .Produces<ActiveModulesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetActiveModulesAsync(
        HttpContext httpContext,
        GetActiveModulesHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!TryResolveConcreteTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var result = await handler.HandleAsync(new GetActiveModulesQuery { TenantId = tenant }, ct);
        return Results.Ok(result);
    }

    /// <summary>
    /// Copia LOCAL del patrón <c>AnalyticsEndpoints.TryResolveEffectiveTenant</c> (archivo ajeno,
    /// no se toca), sin vista global: el SuperAdmin sin <c>tenantId</c> recibe 400 en vez de un
    /// centinela "todas las compañías" (no aplica aquí, los flags son de UN tenant).
    /// Tenant Admin/otro rol pidiendo un tenant ajeno → 403 (AC4).
    /// </summary>
    private static bool TryResolveConcreteTenant(
        ClaimsPrincipal user, Guid? tenantIdQuery, out Guid tenant, out IResult? error)
    {
        tenant = Guid.Empty;
        error = null;

        var isSuperAdmin = user.IsInRole(AdminAuthorization.SuperAdminRole);

        if (tenantIdQuery is { } requested && requested != Guid.Empty)
        {
            // Tenant explícito: SuperAdmin puede acceder a cualquiera; otros solo al propio.
            if (isSuperAdmin) { tenant = requested; return true; }

            if (TryResolveTenantId(user, out var claimTenant) && requested == claimTenant)
            {
                tenant = claimTenant;
                return true;
            }

            error = Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "No está autorizado para consultar los módulos activos de otro tenant.");
            return false;
        }

        if (isSuperAdmin)
        {
            // Sin vista global aquí: el SuperAdmin debe elegir compañía.
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
}
