using System.Security.Claims;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class AttachmentEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        // POST adjunto (multipart/form-data: file + tipo) -> 201 AttachmentDto
        group.MapPost("/instances/{id:guid}/attachments", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromForm] string? tipo,
            IFormFile? file,
            UploadAttachmentHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (file is null || file.Length == 0)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el archivo (file).");

            await using var stream = file.OpenReadStream();
            var input = new UploadAttachmentInput(
                tipo ?? string.Empty,
                file.FileName,
                file.ContentType,
                file.Length,
                stream);

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, input, null, ct);
            return error switch
            {
                "missing_file" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el archivo (file)."),
                "invalid_tipo" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "tipo inválido."),
                "invalid_mime" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Solo se admite PDF."),
                "file_too_large" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El archivo excede el máximo de 20 MB."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden adjuntar documentos en estado borrador."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/attachments/{result!.Id}", result),
            };
        })
        .WithName("UploadProcedureInstanceAttachment")
        .DisableAntiforgery();

        // POST presign: crea una presigned POST policy para subir el binario DIRECTO a S3 desde el
        // navegador (sin pasar por el request del API; resuelve PDFs grandes). -> 200 { storagePath, url, fields }
        group.MapPost("/instances/{id:guid}/attachments/presign", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] PresignAttachmentRequest? body,
            PresignAttachmentHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (body is null)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el cuerpo de la solicitud.");

            var input = new PresignAttachmentInput(
                body.Tipo ?? string.Empty,
                body.Filename ?? string.Empty,
                body.Mimetype ?? string.Empty,
                body.SizeBytes);

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, input, ct);
            return error switch
            {
                "missing_file" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El tamaño del archivo debe ser mayor a 0."),
                "invalid_tipo" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "tipo inválido."),
                "invalid_mime" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Solo se admite PDF."),
                "file_too_large" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El archivo excede el máximo de 20 MB."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden adjuntar documentos en estado borrador."),
                _ => Results.Ok(result),
            };
        }).WithName("PresignProcedureInstanceAttachment");

        // POST register: registra la metadata de un adjunto YA subido a S3 vía presign. -> 201 AttachmentDto
        group.MapPost("/instances/{id:guid}/attachments/register", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] RegisterAttachmentRequest? body,
            RegisterAttachmentHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (body is null)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el cuerpo de la solicitud.");

            var input = new RegisterAttachmentInput(
                body.Tipo ?? string.Empty,
                body.Filename ?? string.Empty,
                body.Mimetype ?? string.Empty,
                body.SizeBytes,
                body.Sha256 ?? string.Empty,
                body.StoragePath ?? string.Empty);

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, input, null, ct);
            return error switch
            {
                "missing_file" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El tamaño del archivo debe ser mayor a 0."),
                "invalid_tipo" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "tipo inválido."),
                "invalid_mime" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Solo se admite PDF."),
                "file_too_large" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El archivo excede el máximo de 20 MB."),
                "missing_storage_path" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta storagePath (id de almacenamiento)."),
                "missing_sha256" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta sha256."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden adjuntar documentos en estado borrador."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/attachments/{result!.Id}", result),
            };
        }).WithName("RegisterProcedureInstanceAttachment");

        // GET lista de adjuntos -> { attachments: [...] }
        group.MapGet("/instances/{id:guid}/attachments", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListAttachmentsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("ListProcedureInstanceAttachments");

        // GET descarga del binario de un adjunto (DF-1) -> stream con Content-Disposition: attachment
        group.MapGet("/instances/{id:guid}/attachments/{attachmentId:guid}/download", async (
            Guid id,
            Guid attachmentId,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            DownloadAttachmentHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, attachmentId, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Attachment not found."),
                "file_missing" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Attachment file not found."),
                _ => Results.File(result!.Content, result.Mimetype, result.Filename),
            };
        }).WithName("DownloadProcedureInstanceAttachment");

        // DELETE adjunto -> 204
        group.MapDelete("/instances/{id:guid}/attachments/{attachmentId:guid}", async (
            Guid id,
            Guid attachmentId,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            DeleteAttachmentHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var error = await handler.HandleAsync(id, tenantId.Value, attachmentId, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "attachment_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Attachment not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden borrar documentos en estado borrador."),
                _ => Results.NoContent(),
            };
        }).WithName("DeleteProcedureInstanceAttachment");

        // POST generar impronta (Kyverum RUNT) con los datos del trámite y adjuntarla -> 201 GenerarImprontaAttachmentResult
        group.MapPost("/instances/{id:guid}/attachments/generate-impronta", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            HttpContext http,
            GenerarImprontaAttachmentHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var userId = ResolveUserId(http.User);
            if (userId is null)
                return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "No se pudo resolver el usuario autenticado.");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, userId.Value, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede generar la impronta en estado borrador."),
                "impronta_ya_existe" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Ya existe un documento de impronta cargado para este trámite."),
                "organismo_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe seleccionar el organismo de tránsito antes de generar la impronta."),
                "identificador_vehiculo_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Falta la placa o el VIN del vehículo para generar la impronta."),
                "documento_propietario_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Falta el documento del propietario para generar la impronta."),
                "operador_no_resuelto" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No se pudo resolver el operador que solicita la impronta."),
                "provider_validation" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "Kyverum RUNT rechazó los datos de la impronta."),
                "provider_unauthorized" => Results.Problem(statusCode: 401, title: "Unauthorized", detail: "Kyverum RUNT rechazó la autenticación."),
                "provider_unavailable" => Results.Problem(statusCode: 502, title: "Bad Gateway", detail: "Kyverum RUNT no está disponible temporalmente."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/attachments/{result!.AttachmentId}", result),
            };
        }).WithName("GenerarProcedureInstanceImpronta");

        // GET checklist computado -> { items, faltanObligatorios, completo }
        group.MapGet("/instances/{id:guid}/checklist", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetChecklistHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "tipologia_not_found" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "La tipología del trámite no está configurada."),
                _ => Results.Ok(result),
            };
        }).WithName("GetProcedureInstanceChecklist");

        return app;
    }

    /// <summary>Id del usuario autenticado (claim <c>sub</c>/NameIdentifier), o null si no resuelve.</summary>
    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>Cuerpo del POST /attachments/presign (JSON): metadata del archivo, sin binario.</summary>
internal sealed record PresignAttachmentRequest(
    string? Tipo,
    string? Filename,
    string? Mimetype,
    long SizeBytes);

/// <summary>Cuerpo del POST /attachments/register (JSON): metadata del adjunto ya subido a S3.</summary>
internal sealed record RegisterAttachmentRequest(
    string? Tipo,
    string? Filename,
    string? Mimetype,
    long SizeBytes,
    string? Sha256,
    string? StoragePath);
