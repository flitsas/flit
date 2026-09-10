using Flit.Admin.Domain.OtMetrics;
using Flit.Analytics.Application.Scheduling;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// CRUD de informes programados con alcance Organismo de Tránsito — Reportes 2.0, HU-D (tercera
/// ola). Mismo motor que <see cref="ReportSchedulesEndpoints"/> (handlers, validación, scheduler);
/// lo único distinto es CÓMO se resuelve el tenant (siempre el dueño de un organismo, ver
/// <see cref="AdminOtSchedulingTenantResolver"/>) y la policy (<c>OtModule</c>: Admin OT sobre su
/// propio organismo, o SuperAdmin con <c>?transitOfficeId=</c> sobre cualquiera).
///
/// <para>Solo admite los 3 tipos propios del organismo (<c>ot_analisis</c>/<c>ot_informe</c>/
/// <c>ot_revisores</c>, uno por pestaña con rango de <c>OtReportsConsole.tsx</c>) o
/// <c>reportType="consulta"</c> con <c>savedQueryScope="ot"</c> (informe de una consulta guardada
/// del organismo — "Programar este informe" desde «Consultas personalizadas»): los 5 tipos de
/// compañía leen trámites por <c>tenant_id</c> del RADICADOR (una empresa), y un tenant de
/// organismo nunca radica nada — programarlos aquí produciría un informe vacío en silencio.</para>
/// </summary>
public static class AdminOtReportSchedulesEndpoints
{
    private static readonly string[] OtReportTypes = ["ot_analisis", "ot_informe", "ot_revisores"];

    private static bool IsValidOtSchedule(ReportScheduleInput input) =>
        OtReportTypes.Contains(input.ReportType)
        || (input.ReportType == "consulta" && input.SavedQueryScope == "ot");

    public static IEndpointRouteBuilder MapAdminOtReportSchedulesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot/report-schedules")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · OT · Informes programados (Reportes 2.0)");

        group.MapGet("/", ListAsync)
            .WithName("AdminOtReportSchedulesList")
            .WithSummary("Lista los informes programados del organismo")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .WithName("AdminOtReportSchedulesCreate")
            .WithSummary("Crea un informe programado del organismo")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("AdminOtReportSchedulesUpdate")
            .WithSummary("Actualiza un informe programado del organismo")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("AdminOtReportSchedulesDelete")
            .WithSummary("Elimina (lógicamente) un informe programado del organismo")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        ListReportSchedulesHandler handler,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (tenant, error) = await AdminOtSchedulingTenantResolver.ResolveAsync(
            httpContext.User, transitOfficeId, otMetrics, ct);
        if (error is not null)
            return error;

        var items = await handler.HandleAsync(tenant, ct);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> CreateAsync(
        HttpContext httpContext,
        ReportScheduleInput input,
        CreateReportScheduleHandler handler,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        if (!IsValidOtSchedule(input))
            return AdminOtSchedulingTenantResolver.ValidationProblem(
                "El tipo de informe debe ser uno de: ot_analisis, ot_informe, ot_revisores; o 'consulta' " +
                "con savedQueryScope 'ot'. Aquí solo se programan informes del propio organismo.");

        var (tenant, error) = await AdminOtSchedulingTenantResolver.ResolveAsync(
            httpContext.User, transitOfficeId, otMetrics, ct);
        if (error is not null)
            return error;

        var (result, err) = await handler.HandleAsync(
            tenant, AdminOtSchedulingTenantResolver.TryResolveUserId(httpContext.User), input, ct);
        if (err is not null)
            return AdminOtSchedulingTenantResolver.ValidationProblem(err);

        return Results.Created($"/api/v1/admin/ot/report-schedules/{result!.Id}", result);
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext httpContext,
        Guid id,
        ReportScheduleInput input,
        UpdateReportScheduleHandler handler,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        if (!IsValidOtSchedule(input))
            return AdminOtSchedulingTenantResolver.ValidationProblem(
                "El tipo de informe debe ser uno de: ot_analisis, ot_informe, ot_revisores; o 'consulta' " +
                "con savedQueryScope 'ot'. Aquí solo se programan informes del propio organismo.");

        var (tenant, error) = await AdminOtSchedulingTenantResolver.ResolveAsync(
            httpContext.User, transitOfficeId, otMetrics, ct);
        if (error is not null)
            return error;

        var (result, err) = await handler.HandleAsync(tenant, id, input, ct);
        return err switch
        {
            "not_found" => AdminOtSchedulingTenantResolver.ScheduleNotFound(),
            not null => AdminOtSchedulingTenantResolver.ValidationProblem(err),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext httpContext,
        Guid id,
        DeleteReportScheduleHandler handler,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (tenant, error) = await AdminOtSchedulingTenantResolver.ResolveAsync(
            httpContext.User, transitOfficeId, otMetrics, ct);
        if (error is not null)
            return error;

        var deleted = await handler.HandleAsync(tenant, id, ct);
        return deleted ? Results.NoContent() : AdminOtSchedulingTenantResolver.ScheduleNotFound();
    }
}
