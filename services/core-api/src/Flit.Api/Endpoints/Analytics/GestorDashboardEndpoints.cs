using System.Security.Claims;
using Flit.Analytics.Application.Queries.GestorDashboard;
using Flit.Api.Authorization;
using Flit.Tramites.Domain.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Inicio del Gestor (rediseño de los 3 dashboards): la «Cola de gestión», que el overview no trae.
/// <c>GET /api/v1/analytics/gestor/cola-inactividad?estado&amp;familia&amp;limit=5</c> → <c>{items, total}</c>.
/// «Trámites por firma» no tiene ruta propia: el Inicio usa el atajo <c>sin_firmas</c> del listado de trámites.
/// Mismo modelo de tenant y permisos que <c>/analytics/overview</c>: el tenant sale del JWT; el SuperAdmin puede fijar
/// <c>tenantId</c> y cualquier otro que pida uno ajeno recibe 403. A diferencia del overview NO hay vista global
/// (SuperAdmin sin <c>tenantId</c> ⇒ 400): son cifras de UNA compañía. Ver por familia (HU #13664) con
/// <see cref="IProcedureFamilyPermissionGate.ReadableFamilyCodes"/>, como el resto de lecturas analíticas.
/// <para>La red de una cabeza de grupo no entra aquí: sus rutas viven bajo <c>/tramites/network</c> con su filtro y su
/// auditoría. El Inicio rotula la cola como de la compañía propia cuando la red está activa.</para>
/// </summary>
public static class GestorDashboardEndpoints
{
    public static IEndpointRouteBuilder MapGestorDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics/gestor")
            .RequireAuthorization()
            .WithTags("Analytics · Inicio del Gestor");

        group.MapGet("/cola-inactividad", GetColaInactividadAsync)
            .RequireAnyModulePermission(ReportesPermissions.Resumen, ReportesPermissions.Read, "dashboard.read")
            .WithName("GestorDashboardColaInactividad")
            .WithSummary("Trámites de la compañía con más días sin movimiento")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetColaInactividadAsync(
        HttpContext httpContext,
        GestorDashboardColaInactividadHandler handler,
        IProcedureFamilyPermissionGate familyGate,
        CancellationToken ct,
        [FromQuery] string? estado = null,
        [FromQuery] string? familia = null,
        [FromQuery] int? limit = null,
        [FromQuery] Guid? tenantId = null)
    {
        // La cola devuelve datos POR FILA (radicado, placa, VIN), no un agregado: además del permiso del Inicio exige el
        // mismo gate que el listado de trámites (`/tramites/instances/search`), que es el Ver por familia (HU #13664).
        // El listado responde vacío a quien no ve ninguna familia; aquí se corta antes, con 403, para no exponer la ruta.
        var familias = familyGate.ReadableFamilyCodes();
        if (familyGate.IsActive && familias is { Count: 0 })
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "No tiene permiso para ver trámites.");

        if (!TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(
            new GestorDashboardColaInactividadQuery(tenant, estado, familia, limit ?? GestorDashboardColaInactividadHandler.DefaultLimit)
            {
                AllowedFamilies = familias,
            },
            ct);
        return err is null ? Results.Ok(result) : MapError(err);
    }

    /// <summary>
    /// Igual que <c>AnalyticsEndpoints.TryResolveEffectiveTenant</c>, salvo que el SuperAdmin sin <c>tenantId</c> no
    /// obtiene la vista global sino un 400: la cola es siempre de una compañía.
    /// </summary>
    private static bool TryResolveTenant(ClaimsPrincipal user, Guid? tenantIdQuery, out Guid tenant, out IResult? error)
    {
        tenant = Guid.Empty;
        error = null;
        var isSuperAdmin = user.IsInRole(AdminAuthorization.SuperAdminRole);

        if (tenantIdQuery is { } requested && requested != Guid.Empty)
        {
            if (isSuperAdmin) { tenant = requested; return true; }
            if (RequestTenantResolver.TryResolveTenantId(user, out var claimTenant) && requested == claimTenant)
            {
                tenant = claimTenant;
                return true;
            }

            error = Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "No está autorizado para consultar métricas de otro tenant.");
            return false;
        }

        if (!isSuperAdmin && RequestTenantResolver.TryResolveTenantId(user, out var userTenant))
        {
            tenant = userTenant;
            return true;
        }

        error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: isSuperAdmin
                ? "Este endpoint requiere especificar un tenantId: el SuperAdmin debe indicar la compañía."
                : "Falta el tenant: el token no incluye tenant_id y no se indicó tenantId.");
        return false;
    }

    private static IResult MapError(string error) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Bad Request",
        detail: error switch
        {
            GestorDashboardErrors.InvalidEstado => "El estado indicado no existe.",
            GestorDashboardErrors.InvalidFamilia => "La familia debe ser 'matriculas', 'traspasos' u 'otros'.",
            _ => "Este endpoint requiere una compañía concreta.",
        });
}
