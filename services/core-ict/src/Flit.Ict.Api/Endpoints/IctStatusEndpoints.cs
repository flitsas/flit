using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Status;
using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// Estado y reproceso de un trámite. Se conservan los PATHS y la FORMA de respuesta de v1 para no
/// romper clientes existentes: <c>GET /api/v1/status-process/byId/{id}</c> devuelve el histórico
/// (statusProcess[]) con códigos numéricos. El <c>{id}</c> es el TransactionFlit que devuelve el
/// registro — el número secuencial (transaction_number, paridad v1) — y por compatibilidad también se
/// acepta el manager_id_transaction propio del gestor (la resolución prioriza el número si es numérico).
/// </summary>
public static class IctStatusEndpoints
{
    private static readonly string[] StatusPaths =
    [
        "/api/v1/status-process/byId/{id}",
        "/api/v1/status-process/transaction/{id}",
        "/api/v1/status-process/{id}",
    ];

    private static readonly string[] ReprocessPaths =
    [
        "/api/v1/transactions/reprocess/{id}",
        "/api/v1/ict/reprocess/{id}",
    ];

    public static IEndpointRouteBuilder MapIctStatusEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var path in StatusPaths)
        {
            app.MapGet(path, GetStatusAsync).RequireAuthorization(IctSecurityExtensions.IctClientPolicy);
        }

        // Estado v2-native (plan §A.9): vocabulario v2 sin códigos numéricos.
        app.MapGet("/api/v1/ict/status/{id}", GetStatusV2Async)
            .RequireAuthorization(IctSecurityExtensions.IctClientPolicy);

        foreach (var path in ReprocessPaths)
        {
            app.MapPost(path, ReprocessAsync).RequireAuthorization(IctSecurityExtensions.IctClientPolicy);
        }

        return app;
    }

    private static async Task<IResult> GetStatusV2Async(
        string id,
        IIctStatusV2Query query,
        ICurrentTenant currentTenant,
        CancellationToken ct)
    {
        if (currentTenant.TenantId is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await query.GetByManagerIdTransactionAsync(id, currentTenant.TenantId.Value, ct);
        return result is null
            ? Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(result);
    }

    private static async Task<IResult> GetStatusAsync(
        string id,
        IStatusProcessV1Query query,
        ICurrentTenant currentTenant,
        CancellationToken ct)
    {
        if (currentTenant.TenantId is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await query.GetByManagerIdTransactionAsync(id, currentTenant.TenantId.Value, ct);
        return result is null
            ? Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(result);
    }

    private static async Task<IResult> ReprocessAsync(string id, ReprocessHandler handler, CancellationToken ct)
    {
        var (ok, error) = await handler.HandleAsync(id, ct);
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
    }
}
