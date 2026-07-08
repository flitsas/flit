using Flit.Analytics.Application.Scheduling;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// CRUD de reglas de alerta + historial de disparos — Reportes 2.0, HU-D (§4.7 del contrato).
/// Mismo modelo de tenant/policy que <see cref="ReportSchedulesEndpoints"/>: policy
/// <see cref="AdminAuthorization.AdminCompanyPolicy"/> en TODOS y tenant CONCRETO obligatorio
/// (SuperAdmin con <c>?tenantId=</c>; sin vista global). Id de otro tenant → 404.
/// </summary>
public static class AlertRulesEndpoints
{
    public static IEndpointRouteBuilder MapAlertRulesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .WithTags("Analytics · Alertas por umbral (Reportes 2.0)");

        group.MapGet("/alert-rules", ListAsync)
            .WithName("AnalyticsAlertRulesList")
            .WithSummary("Lista las reglas de alerta del tenant")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/alert-rules", CreateAsync)
            .WithName("AnalyticsAlertRulesCreate")
            .WithSummary("Crea una regla de alerta")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/alert-rules/{id:guid}", UpdateAsync)
            .WithName("AnalyticsAlertRulesUpdate")
            .WithSummary("Actualiza una regla de alerta")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/alert-rules/{id:guid}", DeleteAsync)
            .WithName("AnalyticsAlertRulesDelete")
            .WithSummary("Elimina (lógicamente) una regla de alerta")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/alert-events", ListEventsAsync)
            .WithName("AnalyticsAlertEventsList")
            .WithSummary("Historial paginado de disparos de alertas (más recientes primero)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        ListAlertRulesHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!SchedulingTenantResolver.TryResolveConcreteTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var items = await handler.HandleAsync(tenant, ct);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> CreateAsync(
        HttpContext httpContext,
        AlertRuleInput input,
        CreateAlertRuleHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!SchedulingTenantResolver.TryResolveConcreteTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(
            tenant, SchedulingTenantResolver.TryResolveUserId(httpContext.User), input, ct);
        if (err is not null)
            return SchedulingTenantResolver.ValidationProblem(err);

        return Results.Created($"/api/v1/analytics/alert-rules/{result!.Id}", result);
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext httpContext,
        Guid id,
        AlertRuleInput input,
        UpdateAlertRuleHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!SchedulingTenantResolver.TryResolveConcreteTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(tenant, id, input, ct);
        return err switch
        {
            "not_found" => SchedulingTenantResolver.AlertRuleNotFound(),
            not null => SchedulingTenantResolver.ValidationProblem(err),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext httpContext,
        Guid id,
        DeleteAlertRuleHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!SchedulingTenantResolver.TryResolveConcreteTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var deleted = await handler.HandleAsync(tenant, id, ct);
        return deleted ? Results.NoContent() : SchedulingTenantResolver.AlertRuleNotFound();
    }

    private static async Task<IResult> ListEventsAsync(
        HttpContext httpContext,
        ListAlertEventsHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? ruleId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] Guid? tenantId = null)
    {
        if (!SchedulingTenantResolver.TryResolveConcreteTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var pageResult = await handler.HandleAsync(tenant, ruleId, page, pageSize, ct);
        return Results.Ok(pageResult);
    }
}
