using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Flit.Api.Authorization;

/// <summary>
/// Personaliza la respuesta 403 cuando la autorización falla por falta de rol
/// devolviendo un cuerpo JSON con el mensaje según la policy (SuperAdmin u ot_admin).
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
            await context.Response
                .WriteAsJsonAsync(new { error = ResolveForbiddenMessage(policy) })
                .ConfigureAwait(false);
            return;
        }

        await _defaultHandler
            .HandleAsync(next, context, policy, authorizeResult)
            .ConfigureAwait(false);
    }

    private static string ResolveForbiddenMessage(AuthorizationPolicy policy)
    {
        var roles = policy.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(r => r.AllowedRoles)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return roles.Contains(AdminAuthorization.OtAdminRole)
            ? AdminAuthorization.OtAdminForbiddenMessage
            : AdminAuthorization.ForbiddenMessage;
    }
}
