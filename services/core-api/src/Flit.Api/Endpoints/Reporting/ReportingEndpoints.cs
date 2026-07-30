using System.Security.Claims;
using System.Text.Json;
using Flit.Analytics.Application.Reporting;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Analytics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Reporting;

/// <summary>Endpoints Reporting V2 (Feature #11076).</summary>
public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingV2Endpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/reporting")
            .RequireAuthorization()
            .WithTags("Reporting V2");

        group.MapGet("/procedures", GetProceduresAsync)
            .RequirePermission("reporting.read")
            .WithName("ReportingProcedures");

        group.MapGet("/procedures/{id:guid}", GetProcedureAsync)
            .RequirePermission("reporting.detail")
            .WithName("ReportingProcedureDetail");

        group.MapGet("/procedures/{id:guid}/audit", GetAuditAsync)
            .RequirePermission("reporting.audit")
            .WithName("ReportingProcedureAudit");

        group.MapGet("/consolidado", GetConsolidadoAsync)
            .RequirePermission("reporting.consolidado")
            .WithName("ReportingConsolidado");

        group.MapGet("/productivity", GetProductivityAsync)
            .RequirePermission("reporting.productivity")
            .WithName("ReportingProductivity");

        group.MapGet("/sla", GetSlaAsync)
            .RequirePermission("reporting.read")
            .WithName("ReportingSla");

        group.MapPost("/exports", RequestExportAsync)
            .RequirePermission("reporting.export")
            .WithName("ReportingRequestExport");

        group.MapGet("/exports", ListExportsAsync)
            .RequirePermission("reporting.export")
            .WithName("ReportingListExports");

        group.MapGet("/exports/{id:guid}", GetExportAsync)
            .RequirePermission("reporting.export")
            .WithName("ReportingGetExport");

        group.MapGet("/exports/{id:guid}/download-url", GetDownloadUrlAsync)
            .RequirePermission("reporting.export.download")
            .WithName("ReportingDownloadUrl");

        group.MapGet("/saved-queries", ListSavedQueriesAsync)
            .RequirePermission("reporting.saved-queries.read")
            .WithName("ReportingSavedQueriesList");

        group.MapPost("/saved-queries", CreateSavedQueryAsync)
            .RequirePermission("reporting.saved-queries.write")
            .WithName("ReportingSavedQueriesCreate");

        group.MapPut("/saved-queries/{id:guid}", UpdateSavedQueryAsync)
            .RequirePermission("reporting.saved-queries.write")
            .WithName("ReportingSavedQueriesUpdate");

        group.MapDelete("/saved-queries/{id:guid}", DeleteSavedQueryAsync)
            .RequirePermission("reporting.saved-queries.write")
            .WithName("ReportingSavedQueriesDelete");

        group.MapGet("/preferences", GetPreferencesAsync)
            .RequirePermission("reporting.dashboard.preferences")
            .WithName("ReportingPreferencesGet");

        group.MapPut("/preferences", PutPreferencesAsync)
            .RequirePermission("reporting.dashboard.preferences")
            .WithName("ReportingPreferencesPut");

        return app;
    }

    private static async Task<IResult> GetProceduresAsync(
        HttpContext http,
        GetReportingProceduresHandler handler,
        CancellationToken ct,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? dateType = null,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] string? procedureType = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(
            tenant, from, to, dateType, transitOfficeId, procedureType, status, search,
            sortBy, sortOrder, page, pageSize, ct).ConfigureAwait(false);

        return err switch
        {
            "invalid_range" => Results.BadRequest(new { code = "INVALID_RANGE" }),
            "date_range_too_wide" => Results.BadRequest(new { code = "DATE_RANGE_TOO_WIDE" }),
            "invalid_date_type" => Results.BadRequest(new { code = "INVALID_DATE_TYPE" }),
            "invalid_sort" => Results.BadRequest(new { code = "INVALID_SORT" }),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> GetProcedureAsync(
        HttpContext http,
        Guid id,
        IReportingReadRepository repo,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        var row = await repo.GetProcedureAsync(tenant, id, ct).ConfigureAwait(false);
        return row is null ? Results.NotFound() : Results.Ok(row);
    }

    private static async Task<IResult> GetAuditAsync(
        HttpContext http,
        Guid id,
        GetReportingAuditHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        var result = await handler.HandleAsync(tenant, id, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetConsolidadoAsync(
        HttpContext http,
        GetConsolidadoHandler handler,
        CancellationToken ct,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? groupBy = null,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        var (result, err) = await handler.HandleAsync(tenant, from, to, groupBy, ct).ConfigureAwait(false);
        return err switch
        {
            "invalid_range" => Results.BadRequest(new { code = "INVALID_RANGE" }),
            "date_range_too_wide" => Results.BadRequest(new { code = "DATE_RANGE_TOO_WIDE" }),
            "invalid_group" => Results.BadRequest(new { code = "INVALID_GROUP" }),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> GetProductivityAsync(
        HttpContext http,
        GetProductivityReportHandler handler,
        CancellationToken ct,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? dimension = null,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        var (result, err) = await handler.HandleAsync(tenant, from, to, dimension, ct).ConfigureAwait(false);
        return err switch
        {
            "invalid_range" => Results.BadRequest(new { code = "INVALID_RANGE" }),
            "date_range_too_wide" => Results.BadRequest(new { code = "DATE_RANGE_TOO_WIDE" }),
            "invalid_dimension" => Results.BadRequest(new { code = "INVALID_DIMENSION" }),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> GetSlaAsync(
        HttpContext http,
        GetSlaReportHandler handler,
        CancellationToken ct,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        var (result, err) = await handler.HandleAsync(tenant, from, to, ct).ConfigureAwait(false);
        return err switch
        {
            "invalid_range" => Results.BadRequest(new { code = "INVALID_RANGE" }),
            "date_range_too_wide" => Results.BadRequest(new { code = "DATE_RANGE_TOO_WIDE" }),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> RequestExportAsync(
        HttpContext http,
        RequestExportHandler handler,
        [FromBody] ExportRequestBody body,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();

        var filtersJson = body.Filters is null
            ? "{}"
            : JsonSerializer.Serialize(body.Filters);

        Guid? correlation = null;
        if (http.Request.Headers.TryGetValue("X-Correlation-Id", out var corr)
            && Guid.TryParse(corr.ToString(), out var cid))
            correlation = cid;

        var (result, err) = await handler.HandleAsync(
            new RequestExportCommand(tenant, userId, body.ReportType ?? "procedures", body.Format ?? "excel", filtersJson, correlation),
            ct).ConfigureAwait(false);

        return err switch
        {
            "export_limit_exceeded" => Results.Conflict(new { code = "EXPORT_LIMIT_EXCEEDED" }),
            "invalid_report_type" => Results.BadRequest(new { code = "INVALID_REPORT_TYPE" }),
            "invalid_format" => Results.BadRequest(new { code = "INVALID_FORMAT" }),
            "invalid_filters" => Results.BadRequest(new { code = "INVALID_FILTERS" }),
            _ => Results.Accepted($"/api/v1/reporting/exports/{result!.Id}", result),
        };
    }

    private static async Task<IResult> ListExportsAsync(
        HttpContext http, GetExportJobHandler handler, CancellationToken ct)
    {
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();
        var items = await handler.ListAsync(userId, ct).ConfigureAwait(false);
        return Results.Ok(new ExportJobsListDto(items));
    }

    private static async Task<IResult> GetExportAsync(
        HttpContext http, Guid id, GetExportJobHandler handler, CancellationToken ct)
    {
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();
        var (job, err) = await handler.HandleForOwnerAsync(id, userId, ct).ConfigureAwait(false);
        return err switch
        {
            "not_found" => Results.NotFound(),
            "forbidden" => Results.Forbid(),
            _ => Results.Ok(job),
        };
    }

    private static async Task<IResult> GetDownloadUrlAsync(
        HttpContext http,
        Guid id,
        GetDownloadUrlHandler handler,
        CancellationToken ct)
    {
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();
        var (result, err) = await handler.HandleAsync(id, userId, ct).ConfigureAwait(false);
        return err switch
        {
            "not_found" => Results.NotFound(),
            "forbidden" => Results.Forbid(),
            "not_ready" => Results.Conflict(new { code = "EXPORT_NOT_READY" }),
            "storage_unavailable" => Results.Json(new { code = "STORAGE_UNAVAILABLE" }, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> ListSavedQueriesAsync(
        HttpContext http,
        ISavedQueryRepository repo,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();
        var items = await repo.ListAsync(tenant, userId, ct).ConfigureAwait(false);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> CreateSavedQueryAsync(
        HttpContext http,
        ISavedQueryRepository repo,
        [FromBody] SavedQueryBody body,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest(new { code = "NAME_REQUIRED" });

        var filtersJson = body.Filters is null ? "{}" : JsonSerializer.Serialize(body.Filters);
        var created = await repo.CreateAsync(tenant, userId, body.Name, body.Description, filtersJson, body.IsShared, ct)
            .ConfigureAwait(false);
        return Results.Created($"/api/v1/reporting/saved-queries/{created.Id}", created);
    }

    private static async Task<IResult> UpdateSavedQueryAsync(
        HttpContext http,
        Guid id,
        ISavedQueryRepository repo,
        [FromBody] SavedQueryBody body,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest(new { code = "NAME_REQUIRED" });

        var filtersJson = body.Filters is null ? "{}" : JsonSerializer.Serialize(body.Filters);
        var updated = await repo.UpdateAsync(tenant, userId, id, body.Name, body.Description, filtersJson, body.IsShared, ct)
            .ConfigureAwait(false);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    private static async Task<IResult> DeleteSavedQueryAsync(
        HttpContext http,
        Guid id,
        ISavedQueryRepository repo,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();
        var ok = await repo.DeleteAsync(tenant, userId, id, ct).ConfigureAwait(false);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> GetPreferencesAsync(
        HttpContext http,
        IDashboardPreferencesRepository repo,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();
        return Results.Ok(await repo.GetAsync(tenant, userId, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> PutPreferencesAsync(
        HttpContext http,
        IDashboardPreferencesRepository repo,
        [FromBody] PreferencesBody body,
        CancellationToken ct,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(http.User, tenantId, out var tenant, out var error))
            return error!;
        if (!TryUserId(http.User, out var userId))
            return Results.Unauthorized();
        var json = body.Config is null ? "{}" : JsonSerializer.Serialize(body.Config);
        return Results.Ok(await repo.UpsertAsync(tenant, userId, json, ct).ConfigureAwait(false));
    }

    private static bool TryUserId(ClaimsPrincipal user, out Guid userId)
    {
        userId = default;
        var raw = user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("uid");
        return Guid.TryParse(raw, out userId);
    }

    public sealed record ExportRequestBody(string? ReportType, string? Format, object? Filters);
    public sealed record SavedQueryBody(string? Name, string? Description, object? Filters, bool IsShared = false);
    public sealed record PreferencesBody(object? Config);
}
