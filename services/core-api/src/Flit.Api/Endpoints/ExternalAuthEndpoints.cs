using System.Globalization;
using Flit.Admin.Application.Integrations.Auth;
using Flit.Api.Authorization;
using Flit.Api.Middleware;
using Flit.Api.RateLimiting;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13087 (Feature #13065, Épica #12737) — <c>POST /api/v1/external/auth/token</c> (contrato v3.1
/// §2): canje de <c>clientId</c>/<c>clientSecret</c> por un pase RS256 de 30 minutos. Anónimo y
/// limitado a 10 peticiones por minuto por IP. Errores en problem+json con <c>code</c>.
/// </summary>
public static class ExternalAuthEndpoints
{
    public static IEndpointRouteBuilder MapExternalAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost($"{ExternalClientAuthorization.RoutePrefix}/auth/token", IssueTokenAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ExternalClientRateLimit.TokenPolicyName)
            .WithTags("External")
            .WithName("ExternalAuthToken")
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> IssueTokenAsync(
        ExternalTokenRequest? request,
        IssueExternalClientTokenHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Features.Get<ExternalAccessDetails>()?.SetRequestedClientId(request?.ClientId); // HU #13086
        var result = await handler.HandleAsync(
            new IssueExternalClientTokenCommand(request?.ClientId, request?.ClientSecret), cancellationToken)
            .ConfigureAwait(false);

        switch (result.Status)
        {
            case ExternalTokenStatus.Issued:
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new ExternalTokenResponse(
                    result.AccessToken!, "Bearer", result.ExpiresInSeconds, result.Scopes ?? []));

            case ExternalTokenStatus.Locked:
                var seconds = Math.Max(1, (int)Math.Ceiling((result.RetryAfter ?? TimeSpan.Zero).TotalSeconds));
                context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                await ExternalProblem.WriteAsync(context, StatusCodes.Status423Locked, "client_locked",
                    "Cliente bloqueado temporalmente por intentos fallidos.", cancellationToken).ConfigureAwait(false);
                return Results.Empty;

            case ExternalTokenStatus.RotationRequired:
                await ExternalProblem.WriteAsync(context, StatusCodes.Status403Forbidden, "secret_rotation_required",
                    "El cliente debe rotar su secreto antes de obtener un pase.", cancellationToken).ConfigureAwait(false);
                return Results.Empty;

            case ExternalTokenStatus.Unavailable:
                await ExternalProblem.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, "external_auth_unavailable",
                    "La emisión de pases externos no está disponible.", cancellationToken).ConfigureAwait(false);
                return Results.Empty;

            default:
                await ExternalProblem.WriteAsync(context, StatusCodes.Status401Unauthorized, "invalid_client",
                    "Credenciales de cliente inválidas.", cancellationToken).ConfigureAwait(false);
                return Results.Empty;
        }
    }
}

/// <summary>Cuerpo de la petición de pase.</summary>
public sealed record ExternalTokenRequest(string? ClientId, string? ClientSecret);

/// <summary>Respuesta del pase (contrato v3.1 §2).</summary>
public sealed record ExternalTokenResponse(string AccessToken, string TokenType, int ExpiresIn, IReadOnlyList<string> Scope);
