using Flit.Admin.Domain.OtMetrics;
using Flit.Analytics.Application.Scheduling;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// CRUD de reglas de alerta + historial de disparos con alcance Organismo de Tránsito — Reportes
/// 2.0, HU-D (tercera ola). Mismo motor que <see cref="AlertRulesEndpoints"/> (handlers,
/// evaluador, scheduler); solo cambia la resolución de tenant (ver
/// <see cref="AdminOtSchedulingTenantResolver"/>) y la policy (<c>OtModule</c>).
///
/// <para>Solo admite las métricas <c>ot_rejection_rate_pct</c>/<c>ot_stuck_count</c>: el resto lee
/// por <c>tenant_id</c> del radicador, y un tenant de organismo nunca radica nada.</para>
/// </summary>
public static class AdminOtAlertRulesEndpoints
{
    private static readonly string[] OtMetrics = ["ot_rejection_rate_pct", "ot_stuck_count"];

    public static IEndpointRouteBuilder MapAdminOtAlertRulesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot/alert-rules")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · OT · Alertas por umbral (Reportes 2.0)");

        group.MapGet("/", ListAsync)
            .WithName("AdminOtAlertRulesList")
            .WithSummary("Lista las reglas de alerta del organismo")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .WithName("AdminOtAlertRulesCreate")
            .WithSummary("Crea una regla de alerta del organismo")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("AdminOtAlertRulesUpdate")
            .WithSummary("Actualiza una regla de alerta del organismo")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("AdminOtAlertRulesDelete")
            .WithSummary("Elimina (lógicamente) una regla de alerta del organismo")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        var events = app
            .MapGroup("/api/v1/admin/ot/alert-events")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · OT · Alertas por umbral (Reportes 2.0)");

        events.MapGet("/", ListEventsAsync)
            .WithName("AdminOtAlertEventsList")
            .WithSummary("Historial paginado de disparos de alertas del organismo (más recientes primero)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        events.MapPost("/{id:guid}/ack", AckEventAsync)
            .WithName("AdminOtAlertEventsAck")
            .WithSummary("Marca un disparo de alerta del organismo como reconocido (set-once)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        ListAlertRulesHandler handler,
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
        AlertRuleInput input,
        CreateAlertRuleHandler handler,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        if (input.Metric is null || !OtMetrics.Contains(input.Metric))
            return AdminOtSchedulingTenantResolver.ValidationProblem(
                "La métrica debe ser una de: ot_rejection_rate_pct, ot_stuck_count.");

        var (tenant, error) = await AdminOtSchedulingTenantResolver.ResolveAsync(
            httpContext.User, transitOfficeId, otMetrics, ct);
        if (error is not null)
            return error;

        var (result, err) = await handler.HandleAsync(
            tenant, AdminOtSchedulingTenantResolver.TryResolveUserId(httpContext.User), input, ct);
        if (err is not null)
            return AdminOtSchedulingTenantResolver.ValidationProblem(err);

        return Results.Created($"/api/v1/admin/ot/alert-rules/{result!.Id}", result);
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext httpContext,
        Guid id,
        AlertRuleInput input,
        UpdateAlertRuleHandler handler,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        if (input.Metric is null || !OtMetrics.Contains(input.Metric))
            return AdminOtSchedulingTenantResolver.ValidationProblem(
                "La métrica debe ser una de: ot_rejection_rate_pct, ot_stuck_count.");

        var (tenant, error) = await AdminOtSchedulingTenantResolver.ResolveAsync(
            httpContext.User, transitOfficeId, otMetrics, ct);
        if (error is not null)
            return error;

        var (result, err) = await handler.HandleAsync(tenant, id, input, ct);
        return err switch
        {
            "not_found" => AdminOtSchedulingTenantResolver.AlertRuleNotFound(),
            not null => AdminOtSchedulingTenantResolver.ValidationProblem(err),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext httpContext,
        Guid id,
        DeleteAlertRuleHandler handler,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (tenant, error) = await AdminOtSchedulingTenantResolver.ResolveAsync(
            httpContext.User, transitOfficeId, otMetrics, ct);
        if (error is not null)
            return error;

        var deleted = await handler.HandleAsync(tenant, id, ct);
        return deleted ? Results.NoContent() : AdminOtSchedulingTenantResolver.AlertRuleNotFound();
    }

    private static async Task<IResult> ListEventsAsync(
        HttpContext httpContext,
        ListAlertEventsHandler handler,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct,
        [FromQuery] Guid? ruleId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (tenant, error) = await AdminOtSchedulingTenantResolver.ResolveAsync(
            httpContext.User, transitOfficeId, otMetrics, ct);
        if (error is not null)
            return error;

        var pageResult = await handler.HandleAsync(tenant, ruleId, page, pageSize, ct);
        return Results.Ok(pageResult);
    }

    private static async Task<IResult> AckEventAsync(
        HttpContext httpContext,
        Guid id,
        AckAlertEventsHandler handler,
        IOtMetricsReadRepository otMetrics,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (tenant, error) = await AdminOtSchedulingTenantResolver.ResolveAsync(
            httpContext.User, transitOfficeId, otMetrics, ct);
        if (error is not null)
            return error;

        var dto = await handler.HandleAsync(
            tenant, id, AdminOtSchedulingTenantResolver.TryResolveUserId(httpContext.User), ct);
        return dto is null ? AdminOtSchedulingTenantResolver.AlertEventNotFound() : Results.Ok(dto);
    }
}
