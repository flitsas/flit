using System.Security.Claims;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Network;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Api.Endpoints.Tramites;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Enums;
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
            // HU #14320 — auditoría del modo «todas las compañías» (SuperAdmin). Sin desenlace publicado
            // (cualquier otra petición de este grupo) el filtro no escribe nada.
            .AddEndpointFilter<NetworkAccessAuditFilter>()
            .WithTags("Analytics · Reportes detallados (Feature #10813)");

        group.MapGet("/procedures", GetProceduresAsync)
            .RequireModulePermission("reportes.detallados.read")
            .WithName("DetailedReportProcedures")
            .WithSummary("Consulta paginada del reporte detallado con filtros por atributos del trámite")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/procedures/export", ExportExcelAsync)
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .RequireModulePermission("reportes.detallados.export")
            .WithName("DetailedReportExportExcel")
            .WithSummary("Exporta a Excel el reporte detallado filtrado")
            .Produces(StatusCodes.Status200OK, contentType: ExcelContentType)
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> GetProceduresAsync(
        HttpContext httpContext,
        DateOnly? from,
        DateOnly? to,
        GetDetailedProceduresHandler handler,
        GetAllCompaniesDetailedProceduresHandler allCompaniesHandler,
        IProcedureFamilyPermissionGate familyGate,
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
        [FromQuery] int? pageSize = null,
        [FromQuery] string? channel = null,
        [FromQuery] Guid? channelTransitOfficeId = null,
        [FromQuery] bool? allCompanies = null)
    {
        // HU #14320 — la compuerta de rol va PRIMERO: otro rol no llega a validar nada ni a tocar la base.
        if (allCompanies == true && AnalyticsEndpointsHelpers.AllCompaniesForbidden(httpContext.User) is { } forbidden)
            return forbidden;

        if (from is null || to is null)
            return AnalyticsEndpointsHelpers.MissingDates();

        if (!AnalyticsEndpointsHelpers.TryParseChannel(channel, out var channelCode, out var channelError))
            return channelError!;

        if (allCompanies == true)
        {
            if (tenantId is { } t && t != Guid.Empty)
                return AnalyticsEndpointsHelpers.AllCompaniesWithTenant();

            var request = new NetworkDetailedReportRequest(
                null, from.Value, to.Value, transitOfficeId, procedureTypeId, category, status,
                referenceNumber, personDocument, personName, hasTransformation, isLeasing)
            {
                AllowedFamilies = familyGate.ReadableFamilyCodes(), // HU #13664 — Ver por familia
                Channel = channelCode,
                ChannelTransitOfficeId = channelTransitOfficeId,
            };
            var (all, allError) = await allCompaniesHandler.HandleAsync(
                request, page ?? 1, pageSize ?? GetDetailedProceduresHandler.DefaultPageSize, ct).ConfigureAwait(false);
            if (allError is not null)
                return AnalyticsEndpointsHelpers.InvalidRange();

            NetworkAccessAuditContext.Publish(httpContext, new NetworkAccessOutcome(
                NetworkAccessVocabulary.Resources.ReportsProcedures, NetworkAccessVocabulary.Results.Ok,
                all!.ReachedTenantIds,
                NetworkReportsEndpoints.FiltersJson(request, page, pageSize, allCompanies: true),
                PlatformWide: true));
            return Results.Ok(all.Response);
        }

        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (result, err) = await handler.HandleAsync(
            new GetDetailedProceduresQuery(
                tenant, from.Value, to.Value, transitOfficeId, procedureTypeId, category, status,
                referenceNumber, personDocument, personName, hasTransformation, isLeasing,
                page ?? 1, pageSize ?? GetDetailedProceduresHandler.DefaultPageSize)
            {
                AllowedFamilies = familyGate.ReadableFamilyCodes(), // HU #13664 — Ver por familia
                Channel = channelCode,
                ChannelTransitOfficeId = channelTransitOfficeId,
            },
            ct).ConfigureAwait(false);

        return err switch
        {
            "invalid_range" => AnalyticsEndpointsHelpers.InvalidRange(),
            _ => Results.Ok(result),
        };
    }

    private static async Task<IResult> ExportExcelAsync(
        HttpContext httpContext,
        DateOnly? from,
        DateOnly? to,
        IDetailedReportExcelExporter exporter,
        ExportAllCompaniesDetailedProceduresHandler allCompaniesHandler,
        IProcedureFamilyPermissionGate familyGate,
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
        [FromQuery] string? channel = null,
        [FromQuery] Guid? channelTransitOfficeId = null,
        [FromQuery] bool? allCompanies = null)
    {
        // HU #14320 — la compuerta de rol va PRIMERO: otro rol no llega a validar nada ni a tocar la base.
        if (allCompanies == true && AnalyticsEndpointsHelpers.AllCompaniesForbidden(httpContext.User) is { } forbidden)
            return forbidden;

        if (from is null || to is null)
            return AnalyticsEndpointsHelpers.MissingDates();

        if (!AnalyticsEndpointsHelpers.TryParseChannel(channel, out var channelCode, out var channelError))
            return channelError!;

        if (allCompanies == true)
        {
            if (tenantId is { } t && t != Guid.Empty)
                return AnalyticsEndpointsHelpers.AllCompaniesWithTenant();

            return await ExportAllCompaniesAsync(
                httpContext, from.Value, to.Value, exporter, allCompaniesHandler,
                new NetworkDetailedReportRequest(
                    null, from.Value, to.Value, transitOfficeId, procedureTypeId, category, status,
                    referenceNumber, personDocument, personName, hasTransformation, isLeasing)
                {
                    AllowedFamilies = familyGate.ReadableFamilyCodes(), // HU #13664 — Ver por familia
                    Channel = channelCode,
                    ChannelTransitOfficeId = channelTransitOfficeId,
                }).ConfigureAwait(false);
        }

        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
            return error!;

        var (filter, err) = ExportDetailedProceduresHandler.Validate(
            new ExportDetailedProceduresQuery(
                tenant, from.Value, to.Value, transitOfficeId, procedureTypeId, category, status,
                referenceNumber, personDocument, personName, hasTransformation, isLeasing)
            {
                AllowedFamilies = familyGate.ReadableFamilyCodes(), // HU #13664 — Ver por familia
                Channel = channelCode,
                ChannelTransitOfficeId = channelTransitOfficeId,
            });

        return err switch
        {
            "invalid_range" => AnalyticsEndpointsHelpers.InvalidRange(),
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
                fileDownloadName: $"reporte_detallado_{from.Value:yyyyMMdd}_{to.Value:yyyyMMdd}.xlsx"),
        };
    }

    /// <summary>
    /// HU #14320 — XLSX del modo «todas las compañías»: el MISMO filtro resuelto que el listado y el generador de la
    /// exportación de red (columna «Compañía»). El archivo se genera ANTES de responder (temporal con borrado al
    /// cerrar) para conocer las compañías alcanzadas cuando corre el filtro de auditoría.
    /// </summary>
    private static async Task<IResult> ExportAllCompaniesAsync(
        HttpContext http,
        DateOnly from,
        DateOnly to,
        IDetailedReportExcelExporter exporter,
        ExportAllCompaniesDetailedProceduresHandler handler,
        NetworkDetailedReportRequest request)
    {
        var (filter, error) = await handler.ResolveAsync(request, http.RequestAborted).ConfigureAwait(false);
        if (error is not null)
            return AnalyticsEndpointsHelpers.InvalidRange();

        var file = new FileStream(
            Path.Combine(Path.GetTempPath(), $"flit-all-companies-report-{Guid.NewGuid():N}.xlsx"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        IReadOnlyList<Guid> reached;
        try
        {
            reached = await exporter.ExportNetworkAsync(file, filter!, http.RequestAborted).ConfigureAwait(false);
            file.Position = 0;
        }
        catch
        {
            await file.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
            NetworkAccessVocabulary.Resources.ReportsExport, NetworkAccessVocabulary.Results.Ok, reached,
            NetworkReportsEndpoints.FiltersJson(request, null, null, allCompanies: true), PlatformWide: true));
        return Results.File(file, ExcelContentType,
            fileDownloadName: $"reporte_detallado_todas_las_companias_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }
}

/// <summary>Helpers compartidos entre endpoints analíticos (tenant + errores 400).</summary>
internal static class AnalyticsEndpointsHelpers
{
    /// <summary>
    /// HU #14319 — valida el filtro <c>channel</c> contra el catálogo <see cref="TramiteCanal"/>: vacío ⇒ sin
    /// filtro; un código ajeno al catálogo ⇒ 400 con el motivo (sin repetir el valor recibido).
    /// </summary>
    public static bool TryParseChannel(string? channel, out string? code, out IResult? error)
    {
        code = string.IsNullOrWhiteSpace(channel) ? null : channel.Trim();
        error = null;
        if (code is null || TramiteCanal.EsValido(code))
            return true;

        code = null;
        error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: $"El canal indicado no pertenece al catálogo. Valores válidos: {string.Join(", ", TramiteCanal.Todos)}.");
        return false;
    }

    /// <summary>HU #14320 — <c>allCompanies</c> es solo del SuperAdmin; otro rol ⇒ 403 sin consultar nada.</summary>
    public static IResult? AllCompaniesForbidden(ClaimsPrincipal user) =>
        RequestTenantResolver.IsSuperAdmin(user)
            ? null
            : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "El modo todas las compañías solo está disponible para el Super Admin.");

    public static IResult AllCompaniesWithTenant() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El modo todas las compañías no admite tenantId.");

    public static bool TryResolveTenant(
        ClaimsPrincipal user, Guid? tenantIdQuery, out Guid tenant, out IResult? error)
    {
        tenant = Guid.Empty;
        error = null;
        var isSuperAdmin = user.IsInRole(AdminAuthorization.SuperAdminRole);

        if (tenantIdQuery is { } requested && requested != Guid.Empty)
        {
            if (isSuperAdmin) { tenant = requested; return true; }
            if (RequestTenantResolver.TryResolveTenantId(user, out var claimTenant) && requested == claimTenant)
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

        if (RequestTenantResolver.TryResolveTenantId(user, out var userTenant))
        {
            tenant = userTenant;
            return true;
        }

        error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "Falta el tenant: el token no incluye tenant_id y no se indicó tenantId.");
        return false;
    }


    public static IResult InvalidRange() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El rango de fechas es inválido: 'from' no puede ser posterior a 'to'.");

    /// <summary>Bug #14010 H-02: fechas obligatorias ausentes, sin el texto interno del binder.</summary>
    public static IResult MissingDates() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "Debes indicar la fecha inicial y la fecha final.");
}
