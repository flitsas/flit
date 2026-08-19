using Flit.Api.Authorization;
using Flit.Infrastructure.Analytics.Scheduling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Reportes de ICT en vivo (HU #11617) — el mismo cálculo que ya arma el Excel del informe
/// programado (<see cref="IctOwnReportDocumentBuilder"/>), expuesto en JSON para verlo en pantalla
/// sin programar nada. Ruta bajo <c>/api/v1/analytics/*</c> y NO bajo <c>/api/v1/ict/*</c> a
/// propósito: ese segundo prefijo lo enruta el Gateway hacia el microservicio core-ict, no hacia
/// core-api — usarlo aquí rompería el ruteo (mismo criterio ya documentado en
/// <c>IctQueriesEndpoints</c>).
///
/// <para><c>/jobs</c> es SuperAdmin-only: <c>ict.job_runs</c> es una tabla GLOBAL de plataforma sin
/// <c>tenant_id</c>, mismo criterio ya aplicado al informe programado "ict_jobs" en
/// <c>ReportSchedulesEndpoints</c>.</para>
/// </summary>
public static class IctReportsEndpoints
{
    public static IEndpointRouteBuilder MapIctReportsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics/ict-reports")
            .RequireAuthorization()
            .WithTags("Analytics · Reportes de ICT en vivo");

        group.MapGet("/novedades", GetNovedadesAsync)
            .WithName("IctReportsNovedades")
            .WithSummary("Novedades de ICT por causa, resumen + detalle")
            .Produces<IctNovedadesReportDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/atascados", GetAtascadosAsync)
            .WithName("IctReportsAtascados")
            .WithSummary("Pre-trámites de ICT atascados en validación ahora mismo")
            .Produces<IctAtascadosReportDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/jobs", GetJobsAsync)
            .WithName("IctReportsJobs")
            .WithSummary("Rendimiento de los jobs del pipeline de ICT (solo SuperAdmin)")
            .Produces<IctJobsReportDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/webhooks", GetWebhooksAsync)
            .WithName("IctReportsWebhooks")
            .WithSummary("Trazabilidad de entrega de webhooks de ICT, resumen + detalle")
            .Produces<IctWebhooksReportDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Exportación: el MISMO Excel que llega adjunto al informe programado, pero bajo demanda —
        // hasta ahora, para tener el archivo había que programar un correo y esperar a que corriera.
        group.MapGet("/novedades/export", ExportNovedadesAsync)
            .WithName("IctReportsNovedadesExport")
            .WithSummary("Descarga en Excel el informe de novedades de ICT")
            .Produces<FileResult>(StatusCodes.Status200OK, ExcelContentType);

        group.MapGet("/atascados/export", ExportAtascadosAsync)
            .WithName("IctReportsAtascadosExport")
            .WithSummary("Descarga en Excel el informe de atascados de ICT")
            .Produces<FileResult>(StatusCodes.Status200OK, ExcelContentType);

        group.MapGet("/jobs/export", ExportJobsAsync)
            .WithName("IctReportsJobsExport")
            .WithSummary("Descarga en Excel el informe de jobs de ICT (solo SuperAdmin)")
            .Produces<FileResult>(StatusCodes.Status200OK, ExcelContentType);

        group.MapGet("/webhooks/export", ExportWebhooksAsync)
            .WithName("IctReportsWebhooksExport")
            .WithSummary("Descarga en Excel el informe de webhooks de ICT")
            .Produces<FileResult>(StatusCodes.Status200OK, ExcelContentType);

        return app;
    }

    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static async Task<IResult> ExportNovedadesAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        IctOwnReportDocumentBuilder builder,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var bytes = await builder.BuildNovedadesAsync(tenant, from, to, ct).ConfigureAwait(false);
        return Results.File(bytes, ExcelContentType, $"ict_novedades_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx");
    }

    private static async Task<IResult> ExportAtascadosAsync(
        HttpContext httpContext,
        IctOwnReportDocumentBuilder builder,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        // El rango se ignora en atascados (siempre es "ahora"): la firma lo pide por uniformidad con
        // los otros 3 informes programados.
        var bytes = await builder.BuildAtascadosAsync(tenant, default, default, ct).ConfigureAwait(false);
        return Results.File(bytes, ExcelContentType, "ict_atascados.xlsx");
    }

    private static async Task<IResult> ExportJobsAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        IctOwnReportDocumentBuilder builder,
        CancellationToken ct)
    {
        if (!httpContext.User.IsInRole(AdminAuthorization.SuperAdminRole))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "El reporte de jobs es de plataforma: solo SuperAdmin puede consultarlo.");
        }

        var bytes = await builder.BuildJobsAsync(Guid.Empty, from, to, ct).ConfigureAwait(false);
        return Results.File(bytes, ExcelContentType, $"ict_jobs_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx");
    }

    private static async Task<IResult> ExportWebhooksAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        IctOwnReportDocumentBuilder builder,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var bytes = await builder.BuildWebhooksAsync(tenant, from, to, ct).ConfigureAwait(false);
        return Results.File(bytes, ExcelContentType, $"ict_webhooks_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx");
    }

    private static async Task<IResult> GetNovedadesAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        IctOwnReportDocumentBuilder builder,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var report = await builder.LoadNovedadesAsync(tenant, from, to, ct).ConfigureAwait(false);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetAtascadosAsync(
        HttpContext httpContext,
        IctOwnReportDocumentBuilder builder,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var report = await builder.LoadAtascadosAsync(tenant, ct).ConfigureAwait(false);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetJobsAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        IctOwnReportDocumentBuilder builder,
        CancellationToken ct)
    {
        if (!httpContext.User.IsInRole(AdminAuthorization.SuperAdminRole))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "El reporte de jobs es de plataforma: solo SuperAdmin puede consultarlo.");
        }

        var report = await builder.LoadJobsAsync(from, to, ct).ConfigureAwait(false);
        return Results.Ok(report);
    }

    private static async Task<IResult> GetWebhooksAsync(
        HttpContext httpContext,
        DateOnly from,
        DateOnly to,
        IctOwnReportDocumentBuilder builder,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var report = await builder.LoadWebhooksAsync(tenant, from, to, ct).ConfigureAwait(false);
        return Results.Ok(report);
    }
}
