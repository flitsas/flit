using System.Security.Claims;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #14454 (Épica #14210, ADR-0082) — reconoce al usuario «solo-caja»: tiene el rol <c>ot_caja</c> y ningún otro. Un usuario con
/// además cualquier otro rol, o sin ninguno, NO lo es: conserva el acceso de siempre (el cambio es aditivo).
/// </summary>
public static class SoloCajaGuard
{
    public static bool EsSoloCaja(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var roles = user.Claims.Where(c => c.Type == AdminAuthorization.RoleClaimType).Select(c => c.Value).ToList();
        return roles.Count > 0
            && roles.TrueForAll(r => string.Equals(r, AdminAuthorization.OtCajaRole, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Segunda capa del guardián solo-caja (la primera es Identidad, que no le emite token de plataforma ni de Trámites): si ya hay un
/// token en circulación cuyos roles son solo <c>ot_caja</c>, todo <c>/api/*</c> responde 403 <c>caja_sin_acceso</c>, salvo la
/// lista de apps de la barra de la suite. Va tras la autenticación y antes de la autorización. Inerte si no existe un usuario
/// <c>ot_caja</c>.
/// </summary>
public sealed class SoloCajaGuardMiddleware(RequestDelegate next)
{
    private static readonly PathString AppsRoute = new("/api/v1/platform/me/apps");

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !context.Request.Path.Equals(AppsRoute, StringComparison.OrdinalIgnoreCase)
            && SoloCajaGuard.EsSoloCaja(context.User))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "caja_sin_acceso",
                message = "Su usuario solo puede cobrar en el portal de caja.",
            }).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
