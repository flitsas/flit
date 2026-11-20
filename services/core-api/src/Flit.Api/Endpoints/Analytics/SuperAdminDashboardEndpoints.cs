using Flit.Analytics.Application.Queries.SuperAdminDashboard;
using Flit.Api.Authorization;
using Flit.Tramites.Domain.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Inicio del SuperAdmin (rediseño de los 3 dashboards, 2026-11-17): lecturas ENTRE TENANTS que no tienen otro dueño.
/// <list type="bullet">
///   <item><c>companias-resumen</c> — «Resumen de compañías activas»: activos, tasa de aprobación y represados.</item>
///   <item><c>organismos</c> — «Rendimiento por Organismo de Tránsito», su distribución por familia y el «Tiempo promedio
///   ecosistema». Existe porque <c>/analytics/ot-metrics</c> no tiene vista global (SuperAdmin sin tenant → 400) y exige
///   rango, mientras el Inicio arranca en «Todas las compañías» y sin fechas (BUG #12588).</item>
/// </list>
/// <para>El gate es la policy declarativa <see cref="AdminAuthorization.SuperAdminPolicy"/> sobre el grupo: cualquier otro
/// rol recibe 403 antes de llegar al handler. Las familias sin Ver se excluyen como en el resto de analítica (HU #13664).</para>
/// </summary>
public static class SuperAdminDashboardEndpoints
{
    public static IEndpointRouteBuilder MapSuperAdminDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics/superadmin")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Analytics · Inicio del SuperAdmin (todas las compañías)");

        group.MapGet("/companias-resumen", GetCompaniesAsync)
            .WithName("SuperAdminDashboardCompanies")
            .WithSummary("Resumen por compañía activa: trámites activos, tasa de aprobación y represados")
            .Produces<SuperAdminDashboardCompaniesDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/organismos", GetOfficesAsync)
            .WithName("SuperAdminDashboardOffices")
            .WithSummary("Volumen, tiempo de decisión en días y distribución por organismo de tránsito")
            .Produces<SuperAdminDashboardOfficesDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetCompaniesAsync(
        [FromServices] SuperAdminDashboardCompaniesHandler handler,
        [FromServices] IProcedureFamilyPermissionGate familyGate,
        CancellationToken ct,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int? stuckDays = null)
    {
        var (result, error) = await handler.HandleAsync(
            new SuperAdminDashboardCompaniesQuery(from, to, stuckDays) { AllowedFamilies = familyGate.ReadableFamilyCodes() },
            ct).ConfigureAwait(false);

        return error is null ? Results.Ok(result) : MapError(error);
    }

    private static async Task<IResult> GetOfficesAsync(
        [FromServices] SuperAdminDashboardOfficesHandler handler,
        [FromServices] IProcedureFamilyPermissionGate familyGate,
        CancellationToken ct,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? tenantId = null)
    {
        var (result, error) = await handler.HandleAsync(
            new SuperAdminDashboardOfficesQuery(tenantId, from, to) { AllowedFamilies = familyGate.ReadableFamilyCodes() },
            ct).ConfigureAwait(false);

        return error is null ? Results.Ok(result) : MapError(error);
    }

    private static IResult MapError(string error) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Bad Request",
        detail: error switch
        {
            "invalid_stuck_days" => "'stuckDays' debe estar entre 1 y 90.",
            _ => "El rango de fechas es inválido: 'from' no puede ser posterior a 'to'.",
        });
}
