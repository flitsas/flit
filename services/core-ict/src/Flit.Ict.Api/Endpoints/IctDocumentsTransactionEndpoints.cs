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
        CancellationToken ct,
        bool preliminar = false)
    {
        // ?preliminar=true: la carpeta del trámite antes de su aprobación (consolidado vigente, con su
        // estado y si es definitiva). Sin el parámetro, la respuesta de siempre: solo trámites aprobados.
        if (preliminar)
        {
            var (entrega, errorPreliminar) = await handler.HandlePreliminarAsync(id, ct);
            return entrega is not null
                ? Results.Ok(new { url = entrega.Url, estado = entrega.Estado, definitivo = entrega.Definitivo })
                : Error(errorPreliminar);
        }

        var (url, error) = await handler.HandleAsync(id, ct);
        return error is null ? Results.Ok(new { url }) : Error(error);
    }

    private static IResult Error(string? error)
    {
        return error switch
        {
            "unauthenticated" =>
                Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized),

            "not_found" or "not_materialized" =>
                Results.Json(new { error }, statusCode: StatusCodes.Status404NotFound),

            "not_approved" or "anulado" or "consolidado_no_disponible" =>
                Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict),

            "storage_unavailable" =>
                Results.Json(new { error }, statusCode: StatusCodes.Status503ServiceUnavailable),

            _ =>
                Results.Json(new { error }, statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
