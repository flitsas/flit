using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Historial de transiciones de estado de una instancia (HU-2 N03, RF05). Paginado, más
/// reciente primero. Protegido por <c>TenantEnforcementMiddleware</c> como el resto de
/// <c>/instances*</c>.
/// </summary>
internal static class StatusHistoryEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesStatusHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapGet("/instances/{id:guid}/status-history", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromQuery(Name = "page")] int? page,
            [FromQuery(Name = "pageSize")] int? pageSize,
            GetStatusHistoryHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, page ?? 1, pageSize ?? 20, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureInstanceStatusHistory");

        return app;
    }
}
