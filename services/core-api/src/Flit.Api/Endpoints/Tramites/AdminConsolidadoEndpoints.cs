using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Gestión avanzada del admin sobre el expediente consolidado de un trámite (Feature #12155,
/// HU #12158): limpiar (forzar regeneración) o cargar un PDF externo que prevalezca sobre la
/// regeneración automática. Rutas y permisos ya catalogados por HU #12157
/// (<see cref="AdminTramiteAuthorization"/>); esta HU implementa los endpoints de negocio.
/// </summary>
internal static class AdminConsolidadoEndpoints
{
    internal static IEndpointRouteBuilder MapAdminTramiteConsolidadoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/tramites").WithTags("Admin · Trámites (gestión avanzada)");

        // POST /api/v1/admin/tramites/{id}/consolidado/limpiar — AC1: SIEMPRE regenera, incluso si el
        // vigente es Source="user" (única vía que puede descartarlo).
        group.MapPost("/{id:guid}/consolidado/limpiar", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            HttpContext http,
            LimpiarConsolidadoHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ResolveUserId(http.User), ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "migrado_solo_lectura" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Trámite migrado (solo lectura): no se limpia el consolidado."),
                SubmitGate.FurRequerido => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe generar el FUR antes del consolidado."),
                "sin_adjuntos" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No hay adjuntos para consolidar."),
                "adjunto_no_disponible" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Un adjunto del expediente no está disponible en almacenamiento."),
                "mimetype_no_soportado" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Un adjunto tiene un formato no soportado para el consolidado."),
                "storage_unavailable" => Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "No se pudo guardar el consolidado en el almacenamiento de archivos. Intenta de nuevo en unos minutos."),
                "organismo_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El organismo de tránsito del trámite no está seleccionado o no está activo en el sistema."),
                null => Results.Ok(result),
                _ => Results.Problem(statusCode: 409, title: "Conflict", detail: $"No se pudo limpiar el expediente consolidado: {error}."),
            };
        })
        .RequirePermission(AdminTramiteAuthorization.LimpiarConsolidadoSlug)
        .WithName("AdminTramiteLimpiarConsolidado")
        .WithSummary("Limpia (fuerza la regeneración de) el consolidado de un trámite")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        // POST /api/v1/admin/tramites/{id}/consolidado/cargar — AC3: registra un PDF externo con
        // Source="user"; prevalece sobre regeneraciones automáticas futuras (AC2).
        group.MapPost("/{id:guid}/consolidado/cargar", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            IFormFile? file,
            HttpContext http,
            CargarConsolidadoExternoHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (file is null || file.Length == 0)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el archivo (file).");

            await using var stream = file.OpenReadStream();
            var input = new CargarConsolidadoExternoInput(file.FileName, file.ContentType, file.Length, stream);

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, input, ResolveUserId(http.User), ct);
            return error switch
            {
                "missing_file" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el archivo (file)."),
                "invalid_mime" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El consolidado externo debe ser un PDF (application/pdf)."),
                "file_too_large" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El archivo excede el tamaño máximo permitido."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "migrado_solo_lectura" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Trámite migrado (solo lectura): no se carga el consolidado."),
                null => Results.Created($"/api/v1/tramites/instances/{id}/attachments", result),
                _ => Results.Problem(statusCode: 409, title: "Conflict", detail: $"No se pudo cargar el expediente consolidado: {error}."),
            };
        })
        .RequirePermission(AdminTramiteAuthorization.CargarConsolidadoSlug)
        .WithName("AdminTramiteCargarConsolidado")
        .WithSummary("Carga un PDF externo como el consolidado de un trámite")
        .DisableAntiforgery()
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>Usuario autenticado (claim <c>sub</c>), para la trazabilidad en <c>ProcedureInstanceEvent</c>.</summary>
    private static Guid? ResolveUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
