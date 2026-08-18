using Flit.Analytics.Application.Scheduling;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// CRUD de informes programados de SuperAdmin sobre TODAS las compañías (Reportes 2.0, HU-D,
/// segunda ola) — el gemelo de <see cref="ReportSchedulesEndpoints"/> pero SIN tenant: reutiliza
/// exactamente los mismos handlers/repositorio (por eso <c>IReportScheduleRepository</c> acepta
/// <c>Guid?</c>), pasando siempre <c>tenantId: null</c>.
///
/// <para>Endpoints separados y no una rama dentro de <see cref="ReportSchedulesEndpoints"/> a
/// propósito, mismo criterio que <see cref="SuperAdminQueriesEndpoints"/>: el flujo normal de una
/// empresa (tenant SIEMPRE concreto, §4.7) no cambia una línea, y el gate de acceso es una policy
/// declarativa (<see cref="AdminAuthorization.SuperAdminPolicy"/>), no un chequeo dentro del
/// handler.</para>
///
/// <para><b>Solo tipo "consulta" con alcance "superadmin".</b> Los otros 5 tipos de informe
/// (resumen/operación/OT/uso/productividad) son intrínsecamente de una compañía — no existe un
/// "resumen de todas las compañías" — así que este grupo los rechaza con 400 en vez de dejar
/// crear un informe que el scheduler nunca podría ejecutar coherentemente.</para>
/// </summary>
public static class SuperAdminReportSchedulesEndpoints
{
    public static IEndpointRouteBuilder MapSuperAdminReportSchedulesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics/report-schedules/superadmin")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Analytics · Informes programados de SuperAdmin (todas las compañías)");

        group.MapGet("/", ListAsync)
            .WithName("SuperAdminReportSchedulesList")
            .WithSummary("Lista los informes programados de alcance SuperAdmin")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .WithName("SuperAdminReportSchedulesCreate")
            .WithSummary("Crea un informe programado sobre una consulta guardada de SuperAdmin")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("SuperAdminReportSchedulesUpdate")
            .WithSummary("Actualiza un informe programado de alcance SuperAdmin")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("SuperAdminReportSchedulesDelete")
            .WithSummary("Elimina (lógicamente) un informe programado de alcance SuperAdmin")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(ListReportSchedulesHandler handler, CancellationToken ct)
    {
        var items = await handler.HandleAsync(tenantId: null, ct);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> CreateAsync(
        HttpContext httpContext, ReportScheduleInput input, CreateReportScheduleHandler handler, CancellationToken ct)
    {
        if (!IsSuperAdminConsulta(input))
            return OnlySuperAdminConsulta();

        var (result, err) = await handler.HandleAsync(
            tenantId: null, SchedulingTenantResolver.TryResolveUserId(httpContext.User), input, ct);
        if (err is not null)
            return SchedulingTenantResolver.ValidationProblem(err);

        return Results.Created($"/api/v1/analytics/report-schedules/superadmin/{result!.Id}", result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, ReportScheduleInput input, UpdateReportScheduleHandler handler, CancellationToken ct)
    {
        if (!IsSuperAdminConsulta(input))
            return OnlySuperAdminConsulta();

        var (result, err) = await handler.HandleAsync(tenantId: null, id, input, ct);
        return err switch
        {
            "not_found" => SchedulingTenantResolver.ScheduleNotFound(),
            not null => SchedulingTenantResolver.ValidationProblem(err),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> DeleteAsync(Guid id, DeleteReportScheduleHandler handler, CancellationToken ct)
    {
        var deleted = await handler.HandleAsync(tenantId: null, id, ct);
        return deleted ? Results.NoContent() : SchedulingTenantResolver.ScheduleNotFound();
    }

    private static bool IsSuperAdminConsulta(ReportScheduleInput input) =>
        input.ReportType == "consulta" && input.SavedQueryScope == "superadmin";

    private static IResult OnlySuperAdminConsulta() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "Este endpoint solo crea informes de tipo 'consulta' con alcance 'superadmin'.");
}
