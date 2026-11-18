using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.ExternalSync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Bug #14604 — carga administrativa de documentos a un trámite en CUALQUIER estado (aprobado, anulado y revocado
/// incluidos). Permiso propio <see cref="AdminTramiteAuthorization.CargarDocumentoSlug"/>; el gate de estado del gestor
/// (<c>AttachmentRules.AllowsUploadInState</c>) no se toca.
/// </summary>
internal static class AdminDocumentoEndpoints
{
    internal static IEndpointRouteBuilder MapAdminTramiteDocumentoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/tramites").WithTags("Admin · Trámites (gestión avanzada)");

        // GET /api/v1/admin/tramites/{id}/documentos/tipos — tipos de documento habilitados para el tipo de trámite.
        group.MapGet("/{id:guid}/documentos/tipos", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            AdminDocumentoTiposHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is null
                ? Results.Ok(result)
                : Results.Problem(statusCode: 404, title: "Not Found", detail: "No se encontró el trámite.");
        })
        .RequirePermission(AdminTramiteAuthorization.CargarDocumentoSlug)
        .WithName("AdminTramiteDocumentoTipos")
        .WithSummary("Tipos de documento que el admin puede cargar al trámite (matriz documental del tipo de trámite)")
        .Produces<IReadOnlyList<AdminDocumentoTipoDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/admin/tramites/{id}/documentos — multipart: file + tipo + observacion → 201.
        group.MapPost("/{id:guid}/documentos", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromForm] string? tipo,
            [FromForm] string? observacion,
            IFormFile? file,
            HttpContext http,
            AdminCargarDocumentoHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (file is null || file.Length == 0)
                return Problem(400, "Bad Request", "missing_file", "Falta el archivo (file).");

            await using var stream = file.OpenReadStream();
            var input = new AdminCargarDocumentoInput(
                tipo ?? string.Empty, observacion, file.FileName, file.ContentType, file.Length, stream);

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, input, ResolveUserId(http.User), ct);
            return error switch
            {
                null => Results.Created($"/api/v1/tramites/instances/{id}/attachments/{result!.AttachmentId}", result),
                "missing_file" => Problem(400, "Bad Request", error, "Falta el archivo (file)."),
                "invalid_tipo" => Problem(400, "Bad Request", error, "Indique un tipo de documento válido."),
                "invalid_mime" => Problem(400, "Bad Request", error, "Tipo de archivo no permitido para este documento."),
                "file_too_large" => Problem(400, "Bad Request", error, "El archivo excede el tamaño máximo permitido para este documento."),
                AdminCargarDocumentoHandler.ObservacionRequerida => Problem(400, "Bad Request", error, "La observación es obligatoria."),
                AdminCargarDocumentoHandler.ObservacionDemasiadoLarga => Problem(400, "Bad Request", error,
                    $"La observación no puede superar {AdminCargarDocumentoHandler.MaxObservacion} caracteres."),
                AdminCargarDocumentoHandler.TipoNoHabilitado => Problem(422, "Unprocessable Entity", error,
                    "Ese tipo de documento no está habilitado para el tipo de este trámite."),
                "not_found" => Problem(404, "Not Found", error, "No se encontró el trámite."),
                ExternalAttachmentRules.BlockedCode => Problem(409, "Conflict", error,
                    "Este documento ya fue cargado y no se puede reemplazar."),
                _ => Problem(409, "Conflict", error, "No se pudo cargar el documento."),
            };
        })
        .RequirePermission(AdminTramiteAuthorization.CargarDocumentoSlug)
        .WithName("AdminTramiteCargarDocumento")
        .WithSummary("Carga un documento a un trámite en cualquier estado (admin FLIT, con observación obligatoria)")
        .DisableAntiforgery()
        .Produces<AdminCargarDocumentoResult>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>ProblemDetails con <c>detail</c> en español y el código estable en <c>error</c>.</summary>
    private static IResult Problem(int status, string title, string? codigo, string detail) =>
        Results.Problem(
            statusCode: status,
            title: codigo ?? title, // el front lee el código de negocio en `title` (convención de revocatoria) y `detail` como respaldo
            detail: detail,
            extensions: new Dictionary<string, object?> { ["error"] = codigo });

    /// <summary>Usuario autenticado (claim <c>sub</c>), para la trazabilidad en <c>ProcedureInstanceEvent</c>.</summary>
    private static Guid? ResolveUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
