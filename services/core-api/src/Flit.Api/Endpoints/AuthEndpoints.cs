using System.Security.Claims;
using System.Text.Json;
using Flit.Modules.Security.Application.Auth.ActivateAccount;
using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Application.Auth.ChangePassword;
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
            catch (AccountSuspendedException)
            {
                // HU #10170 AC2 — bloqueo temporal vigente.
                return Results.Json(
                    new ErrorResponse("ACCOUNT_TEMPORARILY_BLOCKED", "La cuenta está bloqueada temporalmente."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (AllRolesInactiveException)
            {
                // HU #10507 AC2 — todos los roles asignados al usuario están inactivos.
                return Results.Json(
                    new ErrorResponse("ALL_ROLES_INACTIVE", "Todos los roles asignados al usuario están inactivos."),
                    statusCode: StatusCodes.Status403Forbidden);
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

        // HU #10170 AC1 — reset administrativo: el admin restablece la contraseña de un usuario de su ámbito.
        group.MapPost("/admin/reset-password", async (
            [FromBody] AdminResetPasswordRequest request,
            ClaimsPrincipal caller,
            AdminResetPasswordHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantClaim = caller.FindFirstValue("tenant_id");
            var callerTenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : (Guid?)null;
            var roleCode = caller.FindFirstValue("role_code") ?? string.Empty;
            var permissions = caller.FindAll("permissions").Select(c => c.Value).ToList();

            try
            {
                await handler.HandleAsync(
                    new AdminResetPasswordCommand(callerTenantId, roleCode, permissions, request.Email),
                    cancellationToken);

                return Results.Ok(new MessageResponse("Contraseña restablecida; se notificó al usuario."));
            }
            catch (AdminScopeException)
            {
                return Results.Json(
                    new ErrorResponse("FORBIDDEN_SCOPE", "No tiene ámbito sobre este usuario."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (TargetUserNotFoundException)
            {
                return Results.Json(
                    new ErrorResponse("USER_NOT_FOUND", "Usuario no encontrado."),
                    statusCode: StatusCodes.Status404NotFound);
            }
        }).RequireAuthorization();

        // HU #10171 AC1/AC2 — cambio voluntario de contraseña del propio usuario autenticado.
        group.MapPut("/change-password", async (
            [FromBody] ChangePasswordRequest request,
            ClaimsPrincipal user,
            ChangePasswordHandler handler,
            CancellationToken cancellationToken) =>
        {
            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            if (!Guid.TryParse(sub, out var userId))
                return Results.Unauthorized();

            try
            {
                await handler.HandleAsync(
                    new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword),
                    cancellationToken);

                return Results.Ok(new MessageResponse("Contraseña actualizada correctamente."));
            }
            catch (WeakPasswordException)
            {
                return Results.Json(
                    new ErrorResponse("PASSWORD_POLICY_VIOLATION", "La contraseña no cumple la política de complejidad."),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidCurrentPasswordException)
            {
                return Results.Json(
                    new ErrorResponse("INVALID_CURRENT_PASSWORD", "La contraseña actual es incorrecta."),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidCredentialsException)
            {
                return Results.Unauthorized();
            }
        }).RequireAuthorization();

        // HU #10177 AC1/AC2 — activación cuenta onboarding con token de invitación.
        group.MapPost("/activate", async (
            [FromBody] ActivateAccountRequest request,
            ActivateAccountHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await handler.HandleAsync(
                    new ActivateAccountCommand(request.Token, request.Password),
                    cancellationToken);

                return Results.Ok(new MessageResponse("Cuenta activada. Ya puedes iniciar sesión."));
            }
            catch (InvalidInvitationTokenException)
            {
                return Results.Json(
                    new ErrorResponse("INVITATION_INVALID", "El enlace de invitación es inválido o ya fue utilizado."),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (WeakPasswordException)
            {
                return Results.Json(
                    new ErrorResponse("WEAK_PASSWORD", "La contraseña no cumple los requisitos mínimos."),
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        // HU #10616 AC3 — expone TODOS los roles activos (roleId + roleCode de cada uno) y el
        // arreglo completo de permisos (unión de todos los roles, igual que el JWT), no solo el
        // primer rol. El claim "roles" es un array JSON de {id, code} por rol activo (emitido por
        // RsaJwtTokenIssuer, HU #10506); el pipeline de JwtBearer lo expande a un Claim("roles", ...)
        // por elemento — se deserializa cada uno individualmente.
        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
            var email = user.FindFirstValue(ClaimTypes.Email)
                ?? user.FindFirstValue("email");
            var tenantId = user.FindFirstValue("tenant_id");
            var companyName = user.FindFirstValue("company_name") ?? string.Empty;
            var companyNit = user.FindFirstValue("company_nit") ?? string.Empty;
            var entityType = user.FindFirstValue("entity_type") ?? string.Empty;
            var permissions = user.FindAll("permissions").Select(c => c.Value).ToArray();
            var roles = user.FindAll("roles")
                .Select(TryParseRoleClaim)
                .Where(r => r is not null)
                .Select(r => r!)
                .ToArray();

            if (userId is null || email is null || tenantId is null)
                return Results.Unauthorized();

            return Results.Ok(new CurrentUserResponse(
                Guid.Parse(userId),
                email,
                Guid.Parse(tenantId),
                roles,
                permissions,
                companyName,
                companyNit,
                entityType));
        }).RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Deserializa un elemento del claim <c>roles</c> (<c>{"id": "...", "code": "..."}</c>, HU
    /// #10506/#10616). Devuelve <c>null</c> ante un valor inesperado en vez de propagar la
    /// excepción — un claim corrupto no debe tumbar <c>/me</c> con 500.
    /// </summary>
    private static RoleClaimResponse? TryParseRoleClaim(Claim claim)
    {
        try
        {
            using var doc = JsonDocument.Parse(claim.Value);
            var id = doc.RootElement.GetProperty("id").GetGuid();
            var code = doc.RootElement.GetProperty("code").GetString() ?? string.Empty;
            return new RoleClaimResponse(id, code);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed record ActivateAccountRequest(string Token, string Password);

    private sealed record LoginRequest(string Email, string Password);

    private sealed record ForgotPasswordRequest(string Email);

    private sealed record ResetPasswordRequest(string Token, string NewPassword);

    private sealed record AdminResetPasswordRequest(string Email);

    private sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    private sealed record MessageResponse(string Message);

    private sealed record LoginResponse(string AccessToken, int ExpiresInSeconds, string TokenType);

    private sealed record CurrentUserResponse(
        Guid UserId,
        string Email,
        Guid TenantId,
        IReadOnlyList<RoleClaimResponse> Roles,
        IReadOnlyList<string> Permissions,
        string CompanyName,
        string CompanyNit,
        string EntityType);

    /// <summary>Rol activo del usuario, tal como viaja en el JWT (HU #10506/#10616).</summary>
    private sealed record RoleClaimResponse(Guid RoleId, string RoleCode);

    private sealed record ErrorResponse(string Code, string Message);
}
