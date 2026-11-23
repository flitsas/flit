using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Documents;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// HU #14668 Bloque 2 — endpoint de descarga del consolidado por referencia de trámite ICT.
/// Path v1: <c>GET /api/v1/documents-transaction/{id}</c>.
/// </summary>
public static class IctDocumentsTransactionEndpoints
{
    public static IEndpointRouteBuilder MapIctDocumentsTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/documents-transaction/{id}", GetDocumentsTransactionAsync)
            .RequireAuthorization(IctSecurityExtensions.IctClientPolicy);

        return app;
    }

    private static async Task<IResult> GetDocumentsTransactionAsync(
        string id,
        GetDocumentsTransactionHandler handler,
        CancellationToken ct)
    {
        var (url, error) = await handler.HandleAsync(id, ct);

        return error switch
        {
            null => Results.Ok(new { url }),

            "unauthenticated" =>
                Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized),

            "not_found" or "not_materialized" =>
                Results.Json(new { error }, statusCode: StatusCodes.Status404NotFound),

            "not_approved" or "consolidado_no_disponible" =>
                Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict),

            "storage_unavailable" =>
                Results.Json(new { error }, statusCode: StatusCodes.Status503ServiceUnavailable),

            _ =>
                Results.Json(new { error }, statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
