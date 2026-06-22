using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class ActorEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesActorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapGet("/instances/{id:guid}/actors", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetActorsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureInstanceActors");

        group.MapPut("/instances/{id:guid}/actors", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            PutActorsRequest request,
            PutActorsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, request, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden editar actores en estado draft."),
                "invalid_rol" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Rol inválido (use comprador|vendedor)."),
                "invalid_document_type" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "tipoDocumento inválido (use CC|CE|NIT|PAS|TI)."),
                "missing_document_number" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "numeroDocumento es obligatorio."),
                "missing_full_name" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "nombreCompleto es obligatorio."),
                "invalid_email" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "email inválido."),
                "rol_not_allowed" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El rol no está permitido para la modalidad del trámite."),
                "missing_required_rol" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Faltan partes obligatorias para la modalidad del trámite."),
                "duplicate_rol" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Cada rol admite a lo sumo un actor."),
                "partes_duplicadas" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El vendedor y el comprador no pueden compartir documento ni correo."),
                "entity_catalog_missing" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El catálogo de entidades (procedure_entities) no está seedeado."),
                _ => Results.Ok(result)
            };
        }).WithName("PutProcedureInstanceActors");

        return app;
    }
}
