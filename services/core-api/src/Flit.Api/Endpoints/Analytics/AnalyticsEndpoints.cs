using System.Security.Claims;
using Flit.Analytics.Application.Abstractions;
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

        group.MapGet("/procedures", GetProcedureDetailsAsync)
            .WithName("AnalyticsProcedureDetails")
            .WithSummary("Detalle paginado de trámites filtrable por categoría y estado")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/export/excel", ExportExcel)
            .WithName("AnalyticsExportExcel")
            .WithSummary("Exporta a Excel (.xlsx) el detalle de trámites filtrado, por streaming")
            .Produces(StatusCodes.Status200OK, contentType: ExcelContentType)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/export/executive-pdf", ExportExecutivePdfAsync)
            .WithName("AnalyticsExecutivePdf")
            .WithSummary("Genera el PDF de Resumen Ejecutivo del periodo")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/monthly-trend", GetMonthlyTrendAsync)
            .WithName("AnalyticsMonthlyTrend")
            .WithSummary("Tendencia mensual de trámites por categoría (últimos N meses)")
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

        // Guid.Empty es el centinela de "vista global" (SuperAdmin sin tenant) → null para el handler.
        Guid? effectiveTenant = (tenant == Guid.Empty) ? null : tenant;
        var (result, err) = await handler.HandleAsync(
            new GetAnalyticsOverviewQuery(effectiveTenant, from, to), ct);

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

        // Guid.Empty es el centinela de "vista global" (SuperAdmin sin tenant) → null para el handler.
        Guid? effectiveTenant = (tenant == Guid.Empty) ? null : tenant;
        var (items, err) = await handler.HandleAsync(
            new GetTopProducersQuery(effectiveTenant, from, to, limit ?? GetTopProducersHandler.DefaultLimit), ct);

        return err is "invalid_range" ? InvalidRange() : Results.Ok(new { items });
    }

    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static async Task<IResult> ExportExecutivePdfAsync(
        HttpContext httpContext,
        ExecutivePdfRequest request,
        ExportExecutivePdfHandler handler,
        CancellationToken ct)
    {
        // AC3: from (o to) ausente en el cuerpo → 400 antes de resolver tenant o consultar.
        if (request?.From is null || request.To is null)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "El cuerpo debe incluir 'from' y 'to'.");

        if (!TryResolveEffectiveTenant(httpContext.User, request.TenantId, out var tenant, out var error))
            return error!;

        if (tenant == Guid.Empty)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Este endpoint requiere especificar un tenantId: el SuperAdmin debe indicar la compañía.");

        var (pdf, err) = await handler.HandleAsync(
            new ExportExecutivePdfQuery(tenant, request.From.Value, request.To.Value), ct);

        if (err is "invalid_range")
            return InvalidRange();

        var fileName = $"resumen_ejecutivo_{request.From:yyyyMMdd}_{request.To:yyyyMMdd}.pdf";
        return Results.File(pdf!, "application/pdf", fileName);
    }

    private static IResult ExportExcel(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        IProcedureExcelExporter exporter,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? tenantId = null)
    {
        if (!TryResolveEffectiveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        if (tenant == Guid.Empty)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Este endpoint requiere especificar un tenantId: el SuperAdmin debe indicar la compañía.");

        var (filter, err) = ExportProceduresExcelHandler.Validate(
            new ExportProceduresExcelQuery(tenant, from, to, category, status));
        if (err is "invalid_range")
            return InvalidRange();

        var fileName = $"tramites_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx";
        return Results.Stream(
            stream => exporter.ExportAsync(stream, filter!, httpContext.RequestAborted),
            contentType: ExcelContentType,
            fileDownloadName: fileName);
    }

    private static async Task<IResult> GetProcedureDetailsAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        GetProcedureDetailsHandler handler,
        CancellationToken ct,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] Guid? tenantId = null)
    {
        if (!TryResolveEffectiveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        if (tenant == Guid.Empty)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Este endpoint requiere especificar un tenantId: el SuperAdmin debe indicar la compañía.");

        var (result, err) = await handler.HandleAsync(
            new GetProcedureDetailsQuery(
                tenant, from, to, category, status,
                page ?? 1, pageSize ?? GetProcedureDetailsHandler.DefaultPageSize),
            ct);

        return err is "invalid_range" ? InvalidRange() : Results.Ok(result);
    }

    private static async Task<IResult> GetMonthlyTrendAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        GetMonthlyTrendHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        var isSuperAdmin = httpContext.User.IsInRole(AdminAuthorization.SuperAdminRole);

        Guid? effectiveTenant;
        if (tenantId is { } requested && requested != Guid.Empty)
        {
            // tenantId explícito: validar acceso.
            if (!isSuperAdmin)
            {
                if (!TryResolveTenantId(httpContext.User, out var claimTenant) || requested != claimTenant)
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                        detail: "No está autorizado para consultar métricas de otro tenant.");
            }
            effectiveTenant = requested;
        }
        else if (isSuperAdmin)
        {
            // SuperAdmin sin tenantId → vista global de todas las compañías.
            effectiveTenant = null;
        }
        else
        {
            // Usuario normal: usa tenant del token.
            if (!TryResolveTenantId(httpContext.User, out var claimTenant))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                    detail: "Falta el tenant: el token no incluye tenant_id y no se indicó tenantId.");
            effectiveTenant = claimTenant;
        }

        var (items, err) = await handler.HandleAsync(new GetMonthlyTrendQuery(effectiveTenant, from, to), ct);
        return err is "invalid_range" ? InvalidRange() : Results.Ok(new { items });
    }

    /// <summary>
    /// AC1: usa el tenant del token. AC2: el SuperAdmin puede fijar otro tenant por query.
    /// Tenant Admin que pida un tenant ajeno → 403; ausencia total de tenant en usuario no-SuperAdmin → 400.
    /// SuperAdmin sin tenantId explícito → devuelve <see cref="Guid.Empty"/> como centinela de
    /// vista global ("todas las compañías"); los callers que no soporten vista global deben agregar
    /// un guard explícito (→ 400 descriptivo) antes de pasar al handler.
    /// Nota: el JWT del SuperAdmin siempre incluye un tenant_id (el tenant DEMO del seeder); por eso
    /// se evalúa isSuperAdmin ANTES que hasClaim para que el path sin tenantId sea siempre global.
    /// </summary>
    private static bool TryResolveEffectiveTenant(
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

        // Sin tenant explícito: SuperAdmin → vista global (centinela Guid.Empty).
        if (isSuperAdmin) { tenant = Guid.Empty; return true; }

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

    private static IResult InvalidRange() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El rango de fechas es inválido: 'from' no puede ser posterior a 'to'.");
}

/// <summary>
/// Cuerpo del Resumen Ejecutivo (HU #10246). <c>From</c>/<c>To</c> son nullables para detectar
/// su ausencia y responder 400 (AC3). <c>TenantId</c> opcional solo aplica para SuperAdmin (AC2).
/// </summary>
public sealed record ExecutivePdfRequest(DateOnly? From, DateOnly? To, Guid? TenantId);
