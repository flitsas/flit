using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Register;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// Registro por lote de trámites. Se conserva el path v1 <c>POST /api/v1/external-transaction/register</c>
/// (y el alias /api/ict/register de desarrollo) con la misma forma de respuesta v1
/// { TotalRows, TotalRowsProcessed, Detail[] }.
/// </summary>
public static class IctRegisterEndpoints
{
    private static readonly string[] RegisterPaths =
    [
        "/api/v1/external-transaction/register",
        "/api/ict/register",
    ];

    public static IEndpointRouteBuilder MapIctRegisterEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var path in RegisterPaths)
        {
            app.MapPost(path, RegisterAsync).RequireAuthorization(IctSecurityExtensions.IctClientPolicy);
        }

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        List<RegisterRowInput> rows,
        RegisterIctBatchHandler handler,
        CancellationToken ct)
    {
        var (result, error) = await handler.HandleAsync(new RegisterBatchCommand(rows), ct);
        if (error is not null)
        {
            return error switch
            {
                "batch_limit_exceeded" => Results.Json(
                    new { error, totalRows = rows.Count },
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                "unauthenticated" => Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized),
                _ => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest),
            };
        }

        return Results.Ok(result);
    }
}
