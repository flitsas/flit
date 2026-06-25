using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Expediente consolidado (matrícula inicial): fusiona FUR + adjuntos del trámite en un PDF único.
/// </summary>
internal static class ConsolidadoEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesConsolidadoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapPost("/instances/{id:guid}/consolidado", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GenerarConsolidadoHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "modalidad_no_soportada" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El consolidado solo está disponible para matrícula inicial."),
                SubmitGate.FurRequerido => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe generar el FUR antes del consolidado."),
                SubmitGate.DocumentosIncompletos => Results.Problem(statusCode: 409, title: "Conflict", detail: "Sube los documentos obligatorios antes de generar el consolidado."),
                "sin_adjuntos" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No hay adjuntos para consolidar."),
                "adjunto_no_disponible" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Un adjunto del expediente no está disponible en almacenamiento."),
                "mimetype_no_soportado" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Un adjunto tiene un formato no soportado para el consolidado."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/attachments", result),
            };
        }).WithName("GenerarProcedureInstanceConsolidado");

        return app;
    }
}
