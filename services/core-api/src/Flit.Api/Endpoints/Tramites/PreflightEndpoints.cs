using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class PreflightEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesPreflightEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapPost("/instances/{id:guid}/preflight", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            RunPreflightHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede correr preflight en estado draft."),
                _ => Results.Ok(result),
            };
        }).WithName("RunProcedureInstancePreflight");

        group.MapGet("/instances/{id:guid}/preflight", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetPreflightHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result); // result puede ser null (sin preflight aún).
        }).WithName("GetProcedureInstancePreflight");

        return app;
    }
}
