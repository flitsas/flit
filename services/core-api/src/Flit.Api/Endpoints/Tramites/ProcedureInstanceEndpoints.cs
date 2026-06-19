using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class ProcedureInstanceEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapPost("/instances", async (
            CreateProcedureInstanceRequest request,
            CreateProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(request, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "not_published" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El tipo de trámite no está publicado."),
                "reference_conflict" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No se pudo generar un número de referencia único. Reintente."),
                _ => Results.Created($"/api/v1/tramites/instances/{result!.Id}", result)
            };
        }).WithName("CreateProcedureInstance");

        group.MapGet("/instances/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureInstance");

        group.MapPatch("/instances/{id:guid}/field-values", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            PatchFieldValuesRequest request,
            PatchFieldValuesHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, request, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden modificar field_values en estado draft"),
                "unknown_field" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "field_key no corresponde a ningún campo del tipo de trámite."),
                _ => Results.Ok(result)
            };
        }).WithName("PatchProcedureInstanceFieldValues");

        group.MapPost("/instances/{id:guid}/submit", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            SubmitProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La instancia ya fue enviada o no está en draft"),
                "not_published" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El tipo de trámite no está publicado."),
                _ => Results.Ok(result)
            };
        }).WithName("SubmitProcedureInstance");

        return app;
    }
}
