using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Register;

namespace Flit.Ict.Api.Endpoints;

/// <summary>Endpoint de registro por lote de pre-trámites (<c>POST /api/ict/register</c>).</summary>
public static class IctRegisterEndpoints
{
    public static IEndpointRouteBuilder MapIctRegisterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ict").RequireAuthorization(IctSecurityExtensions.IctClientPolicy);

        group.MapPost("/register", async (
            List<RegisterRowInput> rows,
            RegisterIctBatchHandler handler,
            CancellationToken ct) =>
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
        });

        return app;
    }
}
