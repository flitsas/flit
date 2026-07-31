using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Endpoints autenticados de generación del FUR (Slice 7, mock). Genera el FUR (+ compraventa en
/// traspaso) gated por biométrica y lo persiste como adjunto. El listado/descarga de los documentos
/// generados se hace vía el endpoint de adjuntos (GET /instances/{id}/attachments — tipos fur/compraventa).
/// </summary>
internal static class FurEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesFurEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        // POST generar FUR -> 201 { documents: [...] }
        group.MapPost("/instances/{id:guid}/fur", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GenerarFurHandler handler,
            GeneracionDocumentalGestorGuard estadoGuard,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            // HU #11051 — con el trámite aprobado o anulado su documentación es definitiva: el gestor no
            // la regenera. La regeneración interna del sistema no pasa por aquí (ver el guard).
            var estadoError = await estadoGuard.CheckAsync(id, tenantId.Value, ct);
            if (estadoError is not null)
                return GeneracionEstadoProblem.From(estadoError);

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "migrado_solo_lectura" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Trámite migrado (solo lectura): no se regeneran documentos."),
                "biometria_gate" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La biométrica requerida no está aprobada para generar el FUR."),
                "organismo_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe seleccionar el organismo de tránsito antes de generar el FUR."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/attachments", result),
            };
        }).WithName("GenerarProcedureInstanceFur");

        // GET formato de FUR que aplica según la clasificación del vehículo (HU #10924). El backend es la
        // fuente de verdad; el frontend lo muestra. -> 200 { format, vehicleClass }
        group.MapGet("/instances/{id:guid}/fur/template-format", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetFurTemplateFormatHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                _ => Results.Ok(result),
            };
        }).WithName("GetProcedureInstanceFurTemplateFormat");

        return app;
    }
}
