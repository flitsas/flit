using System.Text.Json.Serialization;
using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Attachments;
using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// Adjuntos. Se conservan los PATHS y contrato v1 (<c>/api/v1/transact-attachments/*</c>, multipart)
/// para no romper clientes. Se mantiene el flujo v2 presign→register en <c>/api/v1/pretramites/*</c>.
/// </summary>
public static class IctAttachmentEndpoints
{
    public sealed record PresignRequest(string Filename, string MimeType);

    /// <summary>Respuesta v1 del upload: claves PascalCase EXACTAS ({ Status, Message }).</summary>
    private sealed record UploadV1Result(
        [property: JsonPropertyName("Status")] int Status,
        [property: JsonPropertyName("Message")] string Message);

    private static IResult UploadJson(int status, string message, int httpStatus) =>
        Results.Json(new UploadV1Result(status, message), statusCode: httpStatus);

    public static IEndpointRouteBuilder MapIctAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        MapV1TransactAttachments(app);
        MapV2PreTramiteAttachments(app);
        return app;
    }

    // ---- v1: /api/v1/transact-attachments/* (compatibilidad con clientes existentes) ----
    private static void MapV1TransactAttachments(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/transact-attachments")
            .RequireAuthorization(IctSecurityExtensions.IctClientPolicy);

        group.MapPost("/upload", async (HttpRequest request, UploadAttachmentV1Handler handler, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return UploadJson(0, "Estructura de petición inválida", StatusCodes.Status400BadRequest);
            }

            var form = await request.ReadFormAsync(ct);
            var transactionFlit = form["transactionFlit"].ToString();
            var filename = form["filename"].ToString();
            var idAttachmentRaw = form["idAttachment"].ToString();
            var closedRaw = form["closed"].ToString();
            var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);

            if (string.IsNullOrWhiteSpace(transactionFlit) || string.IsNullOrWhiteSpace(filename)
                || file is null || !int.TryParse(idAttachmentRaw, out var idAttachment))
            {
                return UploadJson(0, "Estructura de petición inválida", StatusCodes.Status400BadRequest);
            }

            var closed = closedRaw.Equals("true", StringComparison.OrdinalIgnoreCase) || closedRaw == "1";
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/pdf" : file.ContentType;

            var (ok, error) = await handler.HandleAsync(
                new UploadAttachmentV1Input(transactionFlit, filename, idAttachment, closed, mimeType, buffer.ToArray()), ct);

            if (ok)
            {
                return UploadJson(1, "Registered Successfully", StatusCodes.Status201Created);
            }

            return error switch
            {
                "file_too_large" => UploadJson(0, "El archivo excede el tamaño permitido (5MB)", StatusCodes.Status422UnprocessableEntity),
                "not_found" => UploadJson(0, "Trámite no encontrado", StatusCodes.Status404NotFound),
                "closed_document" or "already_materialized" => UploadJson(0, "El documento está cerrado", StatusCodes.Status409Conflict),
                "unauthenticated" => UploadJson(0, "No autorizado", StatusCodes.Status401Unauthorized),
                _ => UploadJson(0, "Estructura de petición inválida", StatusCodes.Status400BadRequest),
            };
        }).DisableAntiforgery();

        group.MapGet("/{id}", async (
            string id,
            IPreTramiteRepository preTramites,
            IAttachmentRepository attachments,
            ICurrentTenant currentTenant,
            CancellationToken ct) =>
        {
            if (currentTenant.TenantId is null)
            {
                return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var master = await preTramites.FindByManagerIdTransactionAsync(id, currentTenant.TenantId.Value, ct);
            if (master is null)
            {
                return Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound);
            }

            var list = await attachments.ListAsync(master.Id, currentTenant.TenantId.Value, ct);
            var items = list.Select(a => new
            {
                id = a.Id,
                idAttachment = a.IdAttachment,
                filename = a.Filename,
                size = a.SizeBytes,
                createdAt = a.CreatedAt,
                active = a.DeletedAt == null,
            });
            return Results.Ok(items);
        });

        // Operaciones internas de v1 (asociación al master destino). En v2 la asociación la resuelve la
        // materialización por gRPC; se conservan los paths para no romper clientes que los invoquen.
        group.MapPost("/migrate-file", () => Results.Ok(new { Status = 1, Message = "OK" }));
        group.MapPost("/associate-file", () => Results.Ok(new { Status = 1, Message = "OK" }));
    }

    // ---- v2: /api/v1/pretramites/{id}/attachments/* (flujo presign→register) ----
    private static void MapV2PreTramiteAttachments(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pretramites/{id:guid}/attachments")
            .RequireAuthorization(IctSecurityExtensions.IctClientPolicy);

        group.MapPost("/presign", async (Guid id, PresignRequest body, PresignAttachmentHandler handler, CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(body.Filename, body.MimeType, ct);
            return error is not null
                ? Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest)
                : Results.Ok(result);
        });

        group.MapPost("/", async (Guid id, RegisterAttachmentInput body, RegisterAttachmentHandler handler, CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, body, ct);
            if (error is not null)
            {
                return error switch
                {
                    "not_found" => Results.Json(new { error }, statusCode: StatusCodes.Status404NotFound),
                    "closed_document" or "already_materialized" =>
                        Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict),
                    "unauthenticated" => Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized),
                    _ => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest),
                };
            }

            return Results.Ok(result);
        });

        group.MapGet("/", async (Guid id, ListAttachmentsHandler handler, CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error is not null
                ? Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(result);
        });
    }
}
