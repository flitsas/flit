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
                "invalid_mime" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Tipo MIME no permitido (use pdf/jpeg/png/webp)."),
                "file_too_large" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El archivo excede el máximo de 20 MB."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden adjuntar documentos en estado draft."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/attachments/{result!.Id}", result),
            };
        })
        .WithName("UploadProcedureInstanceAttachment")
        .DisableAntiforgery();

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
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden borrar documentos en estado draft."),
                _ => Results.NoContent(),
            };
        }).WithName("DeleteProcedureInstanceAttachment");

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
}
