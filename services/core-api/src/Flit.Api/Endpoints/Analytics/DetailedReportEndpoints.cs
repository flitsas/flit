using System.Security.Claims;
using Flit.Analytics.Application.Queries;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>Reportes detallados de trámites (Feature #10813, HU #10815/#10816).</summary>
public static class DetailedReportEndpoints
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapDetailedReportEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/detailed-report")
            .RequireAuthorization()
            .WithTags("Analytics · Reportes detallados (Feature #10813)");

        group.MapGet("/procedures", GetProceduresAsync)
            .WithName("DetailedReportProcedures")
            .WithSummary("Consulta paginada del reporte detallado con filtros por atributos del trámite")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/procedures/export", ExportExcelAsync)
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .WithName("DetailedReportExportExcel")
            .WithSummary("Exporta a Excel el reporte detallado filtrado")
            .Produces(StatusCodes.Status200OK, contentType: ExcelContentType)
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> GetProceduresAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        GetDetailedProceduresHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] string? referenceNumber = null,
        [FromQuery] string? personDocument = null,
        [FromQuery] string? personName = null,
        [FromQuery] bool? hasTransformation = null,
        [FromQuery] bool? isLeasing = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(
            new GetDetailedProceduresQuery(
                tenant, from, to, transitOfficeId, procedureTypeId, category, status,
                referenceNumber, personDocument, personName, hasTransformation, isLeasing,
                page ?? 1, pageSize ?? GetDetailedProceduresHandler.DefaultPageSize),
            ct).ConfigureAwait(false);

        return err switch
        {
            "invalid_range" => AnalyticsEndpointsHelpers.InvalidRange(),
            "missing_filters" => AnalyticsEndpointsHelpers.MissingFilters(),
            _ => Results.Ok(result),
        };
    }

    private static IResult ExportExcelAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        IDetailedReportExcelExporter exporter,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] string? referenceNumber = null,
        [FromQuery] string? personDocument = null,
        [FromQuery] string? personName = null,
        [FromQuery] bool? hasTransformation = null,
        [FromQuery] bool? isLeasing = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (filter, err) = ExportDetailedProceduresHandler.Validate(
            new ExportDetailedProceduresQuery(
                tenant, from, to, transitOfficeId, procedureTypeId, category, status,
                referenceNumber, personDocument, personName, hasTransformation, isLeasing));

        return err switch
        {
            "invalid_range" => AnalyticsEndpointsHelpers.InvalidRange(),
            "missing_filters" => AnalyticsEndpointsHelpers.MissingFilters(),
            _ => Results.Stream(
                async stream =>
                {
                    try
                    {
                        await exporter.ExportAsync(stream, filter!, httpContext.RequestAborted)
                            .ConfigureAwait(false);
                    }
                    catch (InvalidOperationException ex) when (ex.Message == "no_records")
                    {
                        throw new BadHttpRequestException("No hay registros para exportar con los filtros indicados.");
                    }
                },
                contentType: ExcelContentType,
                fileDownloadName: $"reporte_detallado_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx"),
        };
    }
}

/// <summary>Helpers compartidos entre endpoints analíticos (tenant + errores 400).</summary>
internal static class AnalyticsEndpointsHelpers
{
    public static bool TryResolveTenant(
        ClaimsPrincipal user, Guid? tenantIdQuery, out Guid tenant, out IResult? error)
    {
        tenant = Guid.Empty;
        error = null;
        var isSuperAdmin = user.IsInRole(AdminAuthorization.SuperAdminRole);

        if (tenantIdQuery is { } requested && requested != Guid.Empty)
        {
            if (isSuperAdmin) { tenant = requested; return true; }
            if (TryResolveTenantId(user, out var claimTenant) && requested == claimTenant)
            {
                tenant = claimTenant;
                return true;
            }

            error = Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "No está autorizado para consultar métricas de otro tenant.");
            return false;
        }

        if (isSuperAdmin)
        {
            error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Este endpoint requiere especificar un tenantId.");
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

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    public static IResult InvalidRange() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El rango de fechas es inválido: 'from' no puede ser posterior a 'to'.");

    public static IResult MissingFilters() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "Faltan filtros mínimos: fechas, tipo de trámite (o categoría) y al menos un atributo adicional.");
}
