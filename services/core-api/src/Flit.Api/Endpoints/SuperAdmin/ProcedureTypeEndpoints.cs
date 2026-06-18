using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.SuperAdmin;

internal static class ProcedureTypeEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/procedure-types", async (
            string? family,
            string? publicationStatus,
            ListProcedureTypesHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(family, publicationStatus, ct);
            return Results.Ok(items);
        }).WithName("ListProcedureTypes");

        group.MapPost("/procedure-types", async (
            CreateProcedureTypeRequest request,
            CreateProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/superadmin/procedure-types/{result.Id}", result);
        }).WithName("CreateProcedureType");

        group.MapGet("/procedure-types/{id:guid}", async (
            Guid id,
            GetProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);
            return result is null
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureType");

        group.MapPut("/procedure-types/{id:guid}", async (
            Guid id,
            UpdateProcedureTypeRequest request,
            UpdateProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, request, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "conflict" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Cannot update a published procedure type."),
                _ => Results.Ok(result)
            };
        }).WithName("UpdateProcedureType");

        group.MapDelete("/procedure-types/{id:guid}", async (
            Guid id,
            DeleteProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var error = await handler.HandleAsync(id, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "conflict" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Cannot delete a procedure type with active instances."),
                _ => Results.NoContent()
            };
        }).WithName("DeleteProcedureType");

        group.MapPost("/procedure-types/{id:guid}/publish", async (
            Guid id,
            PublishProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var (result, error, validationErrors) = await handler.HandleAsync(id, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "already_published" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Procedure type is already published."),
                "validation_failed" => Results.UnprocessableEntity(validationErrors),
                _ => Results.Ok(result)
            };
        }).WithName("PublishProcedureType");

        group.MapPost("/procedure-types/{id:guid}/archive", async (
            Guid id,
            ArchiveProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "already_archived" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Procedure type is already archived."),
                _ => Results.Ok(result)
            };
        }).WithName("ArchiveProcedureType");

        group.MapGet("/procedure-types/{id:guid}/conformation-rules", async (
            Guid id,
            GetConformationRulesHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.")
                : Results.Ok(result);
        }).WithName("GetConformationRules");

        group.MapPut("/procedure-types/{id:guid}/conformation-rules", async (
            Guid id,
            List<ConformationRuleInput> inputs,
            UpsertConformationRulesHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, inputs, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                string e when e.StartsWith("entity_not_found:") =>
                    Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: $"Procedure entity not found: {e[17..]}"),
                _ => Results.Ok(result)
            };
        }).WithName("UpsertConformationRules");

        group.MapGet("/procedure-types/{id:guid}/steps", async (
            Guid id,
            GetProcedureStepsHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureSteps");

        group.MapPut("/procedure-types/{id:guid}/steps", async (
            Guid id,
            List<ProcedureStepInput> inputs,
            UpsertProcedureStepsHandler handler,
            CancellationToken ct) =>
        {
            var (result, error, lockedViolated) = await handler.HandleAsync(id, inputs, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "locked_field_violation" => Results.Problem(
                    statusCode: 409,
                    title: "Conflict",
                    detail: $"Cannot remove locked fields: {string.Join(", ", lockedViolated ?? [])}"),
                _ => Results.Ok(result)
            };
        }).WithName("UpsertProcedureSteps");

        group.MapPost("/procedure-types/{id:guid}/validate", async (
            Guid id,
            ValidateProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.")
                : Results.Ok(result);
        }).WithName("ValidateProcedureType");
    }
}
