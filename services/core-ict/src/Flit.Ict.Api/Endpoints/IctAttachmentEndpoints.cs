using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Attachments;

namespace Flit.Ict.Api.Endpoints;

/// <summary>Adjuntos del pre-trámite (presign -> register -> list) — <c>/api/ict/pretramites/{id}/attachments</c>.</summary>
public static class IctAttachmentEndpoints
{
    public sealed record PresignRequest(string Filename, string MimeType);

    public static IEndpointRouteBuilder MapIctAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ict/pretramites/{id:guid}/attachments")
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

        return app;
    }
}
