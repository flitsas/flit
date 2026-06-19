using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Endpoints autenticados de firma electrónica (Slice 7, mock). Solicitar/listar firmas de la
/// compraventa (traspaso) y simular la firma (mock complete). El portal público de firma es Part B.
/// </summary>
internal static class FirmaEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesFirmaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        // POST solicitar firma de una parte -> 201 { signature }
        group.MapPost("/instances/{id:guid}/signatures", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] SolicitarFirmaInput? body,
            SolicitarFirmaHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (body is null)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el cuerpo de la solicitud.");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, body, ct);
            return error switch
            {
                "parte_invalida" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "parte inválida (use comprador|vendedor)."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "no_aplica" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La firma de la compraventa solo aplica a traspaso."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/signatures/{result!.Id}", result),
            };
        }).WithName("SolicitarProcedureInstanceFirma");

        // GET lista/estado de firmas -> { signatures: [...] }
        group.MapGet("/instances/{id:guid}/signatures", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListFirmasHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("ListProcedureInstanceFirmas");

        // POST simular firma (mock complete) -> 200 { id, estado, pdfPath, sha256 }
        group.MapPost("/instances/{id:guid}/signatures/{sigId:guid}/simulate", async (
            Guid id,
            Guid sigId,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            SimularFirmaHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, sigId, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "signature_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Firma no encontrada."),
                "estado_invalido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La firma no está en estado enviada."),
                _ => Results.Ok(result),
            };
        }).WithName("SimularProcedureInstanceFirma");

        return app;
    }
}
