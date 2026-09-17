using System.Text.Json;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Network;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #12360 (Feature #12257, épica #12235) — reporte detallado de la red de una cabeza de grupo,
/// filtrable por cliente hijo o agregado. Rutas NUEVAS bajo el grupo <c>/api/v1/tramites/network</c>
/// (AC7: <c>/api/v1/detailed-report/*</c> no cambia ni recibe parámetros nuevos; el prefijo hereda
/// <c>GroupHeadReadFilter</c> + <c>NetworkAccessAuditFilter</c> del grupo):
/// <list type="bullet">
///   <item><c>GET /reports/procedures</c> — mismos filtros y paginación que
///   <c>GET /api/v1/detailed-report/procedures</c> + <c>childTenantId</c>; cada fila lleva
///   <c>tenantId</c>/<c>tenantName</c> del cliente dueño y los totales se desglosan por cliente (AC1/AC2).</item>
///   <item><c>GET /reports/procedures/export</c> — XLSX con el MISMO generador OpenXml y las mismas
///   columnas precedidas por «Compañía», sobre el MISMO filtro resuelto que el listado (AC5).</item>
/// </list>
/// <para>
/// El alcance nunca sale de la petición (<see cref="RequestTenantResolver.ScopeFromItems"/>);
/// <c>childTenantId</c> lo acota y uno ajeno responde 403 <c>network_child_out_of_scope</c> SIN consulta
/// (AC3). Un conjunto vacío produce un reporte sin registros, nunca uno sin filtro (AC4). Ni el listado
/// ni el archivo incluyen contenido ni enlaces de documentos o anexos (AC6).
/// </para>
/// <para>
/// Auditoría (HU #12361): un registro por petición con <c>reached_tenant_ids</c> = hijos con filas en el
/// resultado; los filtros de texto libre (radicado, documento, nombre) se registran solo por nombre.
/// El archivo se genera ANTES de responder (temporal con borrado al cerrar) para conocer los hijos
/// alcanzados cuando el filtro de auditoría corre, que es al volver la ruta y no al enviar el cuerpo.
/// </para>
/// </summary>
internal static class NetworkReportsEndpoints
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly JsonSerializerOptions FiltersJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    internal static RouteGroupBuilder MapNetworkReports(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/reports/procedures", async (
            HttpContext http,
            DateOnly from,
            DateOnly to,
            GetNetworkDetailedProceduresHandler handler,
            CancellationToken ct,
            [FromQuery] Guid? childTenantId = null,
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
            [FromQuery] int? pageSize = null) =>
        {
            var request = new NetworkDetailedReportRequest(
                childTenantId, from, to, transitOfficeId, procedureTypeId, category, status,
                referenceNumber, personDocument, personName, hasTransformation, isLeasing);
            var (result, error) = await handler.HandleAsync(
                new GetNetworkDetailedProceduresQuery(
                    RequestTenantResolver.ScopeFromItems(http), request,
                    page ?? 1, pageSize ?? GetDetailedProceduresHandler.DefaultPageSize),
                ct);
            PublishOutcome(http, NetworkAccessVocabulary.Resources.ReportsProcedures, request, page, pageSize, result?.ReachedTenantIds, error);
            return error is not null ? MapError(error) : Results.Ok(result!.Response);
        })
            .WithName("NetworkReportsProcedures")
            .WithSummary("Reporte detallado de la red, paginado, filtrable por cliente hijo o agregado")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/reports/procedures/export", async (
            HttpContext http,
            DateOnly from,
            DateOnly to,
            IDetailedReportExcelExporter exporter,
            CancellationToken ct,
            [FromQuery] Guid? childTenantId = null,
            [FromQuery] Guid? transitOfficeId = null,
            [FromQuery] Guid? procedureTypeId = null,
            [FromQuery] string? category = null,
            [FromQuery] string? status = null,
            [FromQuery] string? referenceNumber = null,
            [FromQuery] string? personDocument = null,
            [FromQuery] string? personName = null,
            [FromQuery] bool? hasTransformation = null,
            [FromQuery] bool? isLeasing = null) =>
        {
            var request = new NetworkDetailedReportRequest(
                childTenantId, from, to, transitOfficeId, procedureTypeId, category, status,
                referenceNumber, personDocument, personName, hasTransformation, isLeasing);
            // AC5 — la misma función de resolución que el listado: mismo conjunto, mismos filtros normalizados.
            var (filter, error) = ExportNetworkDetailedProceduresHandler.Validate(RequestTenantResolver.ScopeFromItems(http), request);
            if (error is not null)
            {
                PublishOutcome(http, NetworkAccessVocabulary.Resources.ReportsExport, request, null, null, null, error);
                return MapError(error);
            }

            var file = new FileStream(
                Path.Combine(Path.GetTempPath(), $"flit-network-report-{Guid.NewGuid():N}.xlsx"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            IReadOnlyList<Guid> reached;
            try
            {
                reached = await exporter.ExportNetworkAsync(file, filter!, ct);
                file.Position = 0;
            }
            catch
            {
                await file.DisposeAsync();
                throw;
            }

            PublishOutcome(http, NetworkAccessVocabulary.Resources.ReportsExport, request, null, null, reached, null);
            return Results.File(file, ExcelContentType, fileDownloadName: $"reporte_red_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
        })
            // HU #12652 — la policy AdminCompany que llevaba esta ruta quedó subsumida por el
            // GroupHeadReadFilter del grupo (rol AdminCompany + alcance de red): se retira para que
            // TODA la familia responda el mismo 403 { error: "network_role_required" } con cuerpo,
            // en vez de un 403 vacío solo en la exportación.
            .WithName("NetworkReportsExportExcel")
            .WithSummary("Exporta a Excel el reporte detallado de la red con el mismo filtro que el listado")
            .Produces(StatusCodes.Status200OK, contentType: ExcelContentType)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return group;
    }

    private static IResult MapError(string error) => error switch
    {
        NetworkAnalyticsScope.InvalidRange => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "El rango de fechas es inválido: 'from' no puede ser posterior a 'to'."),
        _ => Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden),
    };

    /// <summary>
    /// Desenlace para el auditor (HU #12361): con éxito, los hijos con filas en el resultado; con
    /// <c>childTenantId</c> fuera del alcance, el intento rechazado sobre ese hijo. Filtros: solo
    /// identificadores y valores no personales; los de texto libre van por NOMBRE (<c>textFilters</c>).
    /// </summary>
    private static void PublishOutcome(
        HttpContext http,
        string resource,
        NetworkDetailedReportRequest request,
        int? page,
        int? pageSize,
        IReadOnlyList<Guid>? reachedTenantIds,
        string? error)
    {
        var filters = FiltersJson(request, page, pageSize);
        switch (error)
        {
            case null:
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    resource, NetworkAccessVocabulary.Results.Ok, reachedTenantIds ?? [], filters));
                break;
            case NetworkScopePolicy.ChildOutOfScope when request.ChildTenantId is { } child:
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    resource, NetworkAccessVocabulary.Results.Forbidden, [child], filters));
                break;
        }
    }

    internal static string FiltersJson(NetworkDetailedReportRequest request, int? page, int? pageSize)
    {
        var textFilters = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(request.ReferenceNumber)) textFilters.Add("referenceNumber");
        if (!string.IsNullOrWhiteSpace(request.PersonDocument)) textFilters.Add("personDocument");
        if (!string.IsNullOrWhiteSpace(request.PersonName)) textFilters.Add("personName");

        return JsonSerializer.Serialize(new
        {
            childTenantId = request.ChildTenantId,
            from = request.From,
            to = request.To,
            transitOfficeId = request.TransitOfficeId,
            procedureTypeId = request.ProcedureTypeId,
            category = NullIfBlank(request.Category),
            status = NullIfBlank(request.Status),
            hasTransformation = request.HasTransformation,
            isLeasing = request.IsLeasing,
            page,
            pageSize,
            textFilters = textFilters.Count > 0 ? textFilters : null,
        }, FiltersJsonOptions);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
