using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Flit.Api.Authorization;

/// <summary>
/// 403 con cuerpo <c>{ error }</c> para core-identity (Epic #13217). Sus rutas solo exigen SuperAdmin o un alcance de
/// servicio, y para esos casos core-api responde exactamente esto (<c>SuperAdminForbiddenResultHandler</c>, que además
/// distingue políticas de negocio como las del OT). Sin él, core-identity respondería 403 sin cuerpo.
/// </summary>
public sealed class PlatformForbiddenResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden && !context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = AdminAuthorization.ForbiddenMessage }).ConfigureAwait(false);
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }
}
