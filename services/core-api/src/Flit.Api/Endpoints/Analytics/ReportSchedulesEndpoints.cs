using System.Security.Claims;
using Flit.Analytics.Application.Scheduling;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// CRUD de informes programados — Reportes 2.0, HU-D (§4.7 del contrato). TODOS los endpoints
/// exigen <see cref="AdminAuthorization.AdminCompanyPolicy"/> y un tenant CONCRETO: el tenant
/// sale del claim JWT <c>tenant_id</c>; el SuperAdmin DEBE indicar <c>?tenantId=</c> (sin vista
/// global aquí → 400 en español). PUT/DELETE de un id de otro tenant responden 404.
/// </summary>
public static class ReportSchedulesEndpoints
{
    public static IEndpointRouteBuilder MapReportSchedulesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .WithTags("Analytics · Informes programados (Reportes 2.0)");

        group.MapGet("/report-schedules", ListAsync)
            .WithName("AnalyticsReportSchedulesList")
            .WithSummary("Lista los informes programados del tenant")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/report-schedules", CreateAsync)
            .WithName("AnalyticsReportSchedulesCreate")
            .WithSummary("Crea un informe programado")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/report-schedules/{id:guid}", UpdateAsync)
            .WithName("AnalyticsReportSchedulesUpdate")
            .WithSummary("Actualiza un informe programado")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/report-schedules/{id:guid}", DeleteAsync)
            .WithName("AnalyticsReportSchedulesDelete")
            .WithSummary("Elimina (lógicamente) un informe programado")
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
        ReportScheduleInput input,
        CreateReportScheduleHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!SchedulingTenantResolver.TryResolveConcreteTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;
        if (input.SavedQueryScope == "superadmin")
            return SchedulingTenantResolver.SuperAdminScopeNotHere();

        var (result, err) = await handler.HandleAsync(
            tenant, SchedulingTenantResolver.TryResolveUserId(httpContext.User), input, ct);
        if (err is not null)
            return SchedulingTenantResolver.ValidationProblem(err);

        return Results.Created($"/api/v1/analytics/report-schedules/{result!.Id}", result);
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext httpContext,
        Guid id,
        ReportScheduleInput input,
        UpdateReportScheduleHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!SchedulingTenantResolver.TryResolveConcreteTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;
        if (input.SavedQueryScope == "superadmin")
            return SchedulingTenantResolver.SuperAdminScopeNotHere();

        var (result, err) = await handler.HandleAsync(tenant, id, input, ct);
        return err switch
        {
            "not_found" => SchedulingTenantResolver.ScheduleNotFound(),
            not null => SchedulingTenantResolver.ValidationProblem(err),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext httpContext,
        Guid id,
        DeleteReportScheduleHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!SchedulingTenantResolver.TryResolveConcreteTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var deleted = await handler.HandleAsync(tenant, id, ct);
        return deleted ? Results.NoContent() : SchedulingTenantResolver.ScheduleNotFound();
    }
}

/// <summary>
/// Resolución de tenant de los endpoints de programación (HU-D). Copia LOCAL del patrón
/// <c>AnalyticsEndpoints.TryResolveEffectiveTenant</c> (§4: archivo ajeno, no se toca), con la
/// diferencia de que aquí NUNCA hay vista global: el SuperAdmin sin <c>tenantId</c> recibe 400.
/// </summary>
internal static class SchedulingTenantResolver
{
    /// <summary>
    /// Tenant del claim <c>tenant_id</c>; SuperAdmin puede fijar otro por query y DEBE indicarlo
    /// (400 si falta). Tenant Admin que pida un tenant ajeno → 403.
    /// </summary>
    public static bool TryResolveConcreteTenant(
        ClaimsPrincipal user, Guid? tenantIdQuery, out Guid tenant, out IResult? error)
    {
        tenant = Guid.Empty;
        error = null;

        var isSuperAdmin = user.IsInRole(AdminAuthorization.SuperAdminRole);

        if (tenantIdQuery is { } requested && requested != Guid.Empty)
        {
            if (isSuperAdmin)
            {
                tenant = requested;
                return true;
            }

            if (TryResolveTenantId(user, out var claimTenant) && requested == claimTenant)
            {
                tenant = claimTenant;
                return true;
            }

            error = Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "No está autorizado para administrar la programación de otro tenant.");
            return false;
        }

        if (isSuperAdmin)
        {
            // Sin vista global en programación/alertas: el SuperAdmin debe elegir compañía.
            error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Este endpoint requiere especificar un tenantId: el SuperAdmin debe indicar la compañía.");
            return false;
        }

        if (TryResolveTenantId(user, out var userTenant))
        {
            tenant = userTenant;
            return true;
        }

        error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "Falta el tenant: el token no incluye tenant_id y no se indicó tenantId.");
        return false;
    }

    public static Guid? TryResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static IResult ValidationProblem(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: detail);

    public static IResult ScheduleNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
            detail: "El informe programado no existe.");

    /// <summary>Alcance "superadmin" solo se crea/edita por <c>SuperAdminReportSchedulesEndpoints</c>
    /// (sin tenant) — este grupo SIEMPRE exige un tenant concreto (§4.7), así que aceptarlo aquí
    /// terminaría violando el CHECK de BD (tenant_id obligatorio cuando el alcance es "empresa").</summary>
    public static IResult SuperAdminScopeNotHere() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El alcance 'superadmin' se programa desde /report-schedules/superadmin, sin tenant.");

    public static IResult AlertRuleNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
            detail: "La regla de alerta no existe.");

    public static IResult AlertEventNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
            detail: "El disparo de alerta no existe.");

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }
}
