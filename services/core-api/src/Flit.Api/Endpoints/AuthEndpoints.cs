using System.Security.Claims;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Application.Auth.ResetPassword;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            LoginHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new LoginCommand(request.Email, request.Password),
                    cancellationToken);

                return Results.Ok(new LoginResponse(
                    result.AccessToken,
                    result.ExpiresInSeconds,
                    result.TokenType));
            }
            catch (InvalidCredentialsException)
            {
                return Results.Json(
                    new ErrorResponse("INVALID_CREDENTIALS", "Invalid credentials."),
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        // HU #10169 AC1/AC2 — solicitud de recuperación. Siempre 202 genérico (anti-enumeración).
        group.MapPost("/forgot-password", async (
            [FromBody] ForgotPasswordRequest request,
            ForgotPasswordHandler handler,
            CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(new ForgotPasswordCommand(request.Email), cancellationToken);

            return Results.Json(
                new MessageResponse("Si el correo está registrado, enviaremos instrucciones de recuperación."),
                statusCode: StatusCodes.Status202Accepted);
        });

        // HU #10169 — redención del token: fija la nueva contraseña.
        group.MapPost("/reset-password", async (
            [FromBody] ResetPasswordRequest request,
            ResetPasswordHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await handler.HandleAsync(
                    new ResetPasswordCommand(request.Token, request.NewPassword),
                    cancellationToken);

                return Results.Ok(new MessageResponse("Contraseña actualizada correctamente."));
            }
            catch (InvalidResetTokenException)
            {
                return Results.Json(
                    new ErrorResponse("INVALID_RESET_TOKEN", "El enlace de recuperación es inválido o expiró."),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (WeakPasswordException)
            {
                return Results.Json(
                    new ErrorResponse("WEAK_PASSWORD", "La contraseña no cumple los requisitos mínimos."),
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
            var email = user.FindFirstValue(ClaimTypes.Email)
                ?? user.FindFirstValue("email");
            var tenantId = user.FindFirstValue("tenant_id");
            var roleId = user.FindFirstValue("role_id");
            var roleCode = user.FindFirstValue("role_code");
            var permissions = user.FindAll("permissions").Select(c => c.Value).ToArray();

            if (userId is null || email is null || tenantId is null || roleId is null)
                return Results.Unauthorized();

            return Results.Ok(new CurrentUserResponse(
                Guid.Parse(userId),
                email,
                Guid.Parse(tenantId),
                Guid.Parse(roleId),
                roleCode ?? string.Empty,
                permissions));
        }).RequireAuthorization();

        return app;
    }

    private sealed record LoginRequest(string Email, string Password);

    private sealed record ForgotPasswordRequest(string Email);

    private sealed record ResetPasswordRequest(string Token, string NewPassword);

    private sealed record MessageResponse(string Message);

    private sealed record LoginResponse(string AccessToken, int ExpiresInSeconds, string TokenType);

    private sealed record CurrentUserResponse(
        Guid UserId,
        string Email,
        Guid TenantId,
        Guid RoleId,
        string RoleCode,
        IReadOnlyList<string> Permissions);

    private sealed record ErrorResponse(string Code, string Message);
}
