using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Flit.Api.Authorization;

/// <summary>
/// Personaliza la respuesta 403 cuando la autorización falla por falta de rol
/// (usuario autenticado pero sin SuperAdmin) devolviendo el cuerpo JSON
/// <c>{ error: "Acceso restringido: se requiere rol SuperAdmin" }</c> (AC3).
///
/// El caso "no autenticado" (challenge → 401, AC4) se delega al handler por
/// defecto, que no escribe cuerpo.
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
            await context.Response
                .WriteAsJsonAsync(new { error = AdminAuthorization.ForbiddenMessage })
                .ConfigureAwait(false);
            return;
        }

        await _defaultHandler
            .HandleAsync(next, context, policy, authorizeResult)
            .ConfigureAwait(false);
    }
}
