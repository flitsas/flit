using Flit.Ict.Application.Auth.Login;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// Login ICT independiente. Se conserva el path v1 <c>POST /api/v1/auth/login</c> para no romper
/// clientes existentes. El tráfico ICT se distingue del de plataforma por host (ict.*) en el Gateway.
/// </summary>
public static class IctAuthEndpoints
{
    /// <summary>Body del login. <c>companyManagerId</c> se acepta por compat v1 y se ignora.</summary>
    public sealed record LoginRequest(string Username, string Password, long? CompanyManagerId);

    private static readonly string[] LoginPaths = ["/api/v1/auth/login"];

    public static IEndpointRouteBuilder MapIctAuthEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var path in LoginPaths)
        {
            app.MapPost(path, LoginAsync).AllowAnonymous();
        }

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        LoginIntegrationClientHandler handler,
        CancellationToken ct)
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
    }
}
