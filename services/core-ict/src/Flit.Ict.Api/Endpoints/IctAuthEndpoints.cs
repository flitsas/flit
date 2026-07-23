using Flit.Ict.Application.Auth.Login;

namespace Flit.Ict.Api.Endpoints;

/// <summary>Endpoints del login ICT independiente (<c>/api/ict/auth</c>).</summary>
public static class IctAuthEndpoints
{
    /// <summary>Body del login. <c>companyManagerId</c> se acepta por compat v1 y se ignora.</summary>
    public sealed record LoginRequest(string Username, string Password, long? CompanyManagerId);

    public static IEndpointRouteBuilder MapIctAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ict/auth").AllowAnonymous();

        group.MapPost("/login", async (
            LoginRequest request,
            LoginIntegrationClientHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(
                new LoginIntegrationClientCommand(request.Username, request.Password, request.CompanyManagerId),
                ct);

            if (error is not null)
            {
                return error switch
                {
                    "invalid_credentials" => Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized),
                    "inactive" or "locked" or "tenant_unavailable" =>
                        Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest),
                };
            }

            if (result!.MustRotate)
            {
                return Results.Json(new { mustRotate = true }, statusCode: StatusCodes.Status200OK);
            }

            return Results.Ok(new { token = result.Token, expiresInSeconds = result.ExpiresInSeconds });
        });

        return app;
    }
}
