using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Flit.Api.Authorization;

/// <summary>
/// Personaliza la respuesta 403 cuando la autorización falla, diferenciando entre:
/// <list type="bullet">
///   <item>Fallo por <see cref="PermissionRequirement"/> (HU #10165, AC3):
///         <c>{ code: "FORBIDDEN", message: "Permisos insuficientes" }</c></item>
///   <item>Fallo por rol SuperAdmin u otras policies:
///         <c>{ error: "Acceso restringido: se requiere rol SuperAdmin" }</c></item>
/// </list>
///
/// El caso "no autenticado" (challenge → 401) se delega al handler por defecto.
/// </summary>
public sealed class SuperAdminForbiddenResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden && !context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            // Si el fallo incluye un PermissionRequirement, usar el formato de permisos (HU #10165).
            var failedRequirements = authorizeResult.AuthorizationFailure?.FailedRequirements;
            var isPermissionFailure = failedRequirements is not null
                && failedRequirements.OfType<PermissionRequirement>().Any();

            if (isPermissionFailure)
            {
                await context.Response
                    .WriteAsJsonAsync(new
                    {
                        code = "FORBIDDEN",
                        message = "Permisos insuficientes",
                    })
                    .ConfigureAwait(false);
            }
            else
            {
                await context.Response
                    .WriteAsJsonAsync(new { error = AdminAuthorization.ForbiddenMessage })
                    .ConfigureAwait(false);
            }

            return;
        }

        await _defaultHandler
            .HandleAsync(next, context, policy, authorizeResult)
            .ConfigureAwait(false);
    }
}
