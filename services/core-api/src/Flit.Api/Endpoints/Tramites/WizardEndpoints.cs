using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class WizardEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesWizardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapGet("/instances/{id:guid}/wizard", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetWizardStateHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureInstanceWizardState");

        return app;
    }
}
