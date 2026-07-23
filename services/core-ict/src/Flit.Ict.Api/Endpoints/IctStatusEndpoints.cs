using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Status;

namespace Flit.Ict.Api.Endpoints;

/// <summary>Estado y reproceso de un pre-trámite (<c>/api/ict/status</c>, <c>/api/ict/reprocess</c>).</summary>
public static class IctStatusEndpoints
{
    public static IEndpointRouteBuilder MapIctStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ict").RequireAuthorization(IctSecurityExtensions.IctClientPolicy);

        group.MapGet("/status/{managerIdTransaction}", async (
            string managerIdTransaction,
            StatusQueryHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(managerIdTransaction, ct);
            return error switch
            {
                null => Results.Ok(result),
                "not_found" => Results.Json(new { error }, statusCode: StatusCodes.Status404NotFound),
                "unauthenticated" => Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized),
                _ => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest),
            };
        });

        group.MapPost("/reprocess/{managerIdTransaction}", async (
            string managerIdTransaction,
            ReprocessHandler handler,
            CancellationToken ct) =>
        {
            var (ok, error) = await handler.HandleAsync(managerIdTransaction, ct);
            if (ok)
            {
                return Results.Ok(new { reprocessed = true });
            }

            return error switch
            {
                "not_found" => Results.Json(new { error }, statusCode: StatusCodes.Status404NotFound),
                "already_materialized" or "not_in_novelty" =>
                    Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict),
                "unauthenticated" => Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized),
                _ => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest),
            };
        });

        return app;
    }
}
