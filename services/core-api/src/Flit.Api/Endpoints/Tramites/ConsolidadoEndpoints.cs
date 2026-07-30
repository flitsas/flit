using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Expediente consolidado (matrícula inicial y traspaso): fusiona FUR + adjuntos del trámite
/// en un PDF único (en traspaso incluye el contrato de compraventa).
/// </summary>
internal static class ConsolidadoEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesConsolidadoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapPost("/instances/{id:guid}/consolidado", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            HttpContext http,
            GenerarConsolidadoHandler handler,
            GeneracionDocumentalGestorGuard estadoGuard,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            // HU #11051 — con el trámite aprobado o anulado el expediente es definitivo: el gestor no lo
            // regenera. El OT sí sigue regenerándolo tras aprobar, por su propia ruta (AdminOtEndpoints),
            // que NO pasa por este gate.
            var estadoError = await estadoGuard.CheckAsync(id, tenantId.Value, ct);
            if (estadoError is not null)
                return GeneracionEstadoProblem.From(estadoError);

            // HU #11017 - el usuario habilita la generacion en cascada de la impronta (el proveedor la exige).
            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ResolveUserId(http.User), ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "migrado_solo_lectura" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Trámite migrado (solo lectura): no se regenera el consolidado."),
                "modalidad_no_soportada" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El consolidado solo está disponible para matrícula inicial y traspaso."),
                SubmitGate.FurRequerido => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe generar el FUR antes del consolidado."),
                "sin_adjuntos" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No hay adjuntos para consolidar."),
                "adjunto_no_disponible" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Un adjunto del expediente no está disponible en almacenamiento."),
                "mimetype_no_soportado" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Un adjunto tiene un formato no soportado para el consolidado."),
                // Éxito SOLO cuando no hay error. El comodín anterior (`_ => Created`) trataba cualquier
                // código NO MAPEADO como éxito y devolvía 201 con cuerpo nulo: el gestor veía la
                // operación "correcta", sin consolidado y sin motivo. Es lo que pasaba con
                // `organismo_requerido` (organismo de tránsito inactivo o sin seleccionar), que este
                // switch no contemplaba aunque el generador del FUR sí lo devuelve.
                null => Results.Created($"/api/v1/tramites/instances/{id}/attachments", result),
                "organismo_requerido" => Results.Problem(
                    statusCode: 409,
                    title: "Conflict",
                    detail: "El organismo de tránsito del trámite no está seleccionado o no está activo "
                        + "en el sistema. Verifícalo antes de generar el expediente consolidado."),
                // Cualquier otro código viaja tal cual: es mejor un motivo técnico que un éxito falso.
                _ => Results.Problem(
                    statusCode: 409,
                    title: "Conflict",
                    detail: $"No se pudo generar el expediente consolidado: {error}."),
            };
        }).WithName("GenerarProcedureInstanceConsolidado");

        return app;
    }

    /// <summary>Usuario autenticado (claim <c>sub</c>), para la generación en cascada de la impronta.</summary>
    private static Guid? ResolveUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
