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
/// Segunda capa del guardián solo-caja (la primera es la autorización de cada ruta: <c>OtModuleAuthorizationHandler</c> le niega todo
/// módulo del organismo). La caja es un módulo de FLIT 2.0 y el usuario <c>ot_caja</c> inicia sesión con el login normal, pero con un
/// token cuyos roles son solo <c>ot_caja</c> la API admite únicamente: la caja (<c>/api/v1/ot/caja</c>), su perfil (<c>/api/v1/me</c>),
/// la sesión (<c>/api/v1/auth</c>, salvo <c>/api/v1/auth/admin</c>), lo mínimo del shell para pintar el menú y la marca
/// (<c>/api/v1/platform/me/apps</c>, <c>/api/v1/public</c> y la lectura de <c>/api/v1/company/branding</c>). Todo lo demás responde 403
/// <c>caja_sin_acceso</c>. Va tras la autenticación y antes de la autorización. Inerte si no existe un usuario <c>ot_caja</c>.
/// </summary>
public sealed class SoloCajaGuardMiddleware(RequestDelegate next)
{
    private static readonly PathString[] RutasPermitidas =
    [
        new("/api/v1/ot/caja"),
        new("/api/v1/me"),
        new("/api/v1/auth"),
        new("/api/v1/platform/me/apps"),
        new("/api/v1/public"),
    ];

    private static readonly PathString AuthAdmin = new("/api/v1/auth/admin");
    private static readonly PathString Branding = new("/api/v1/company/branding");

    /// <summary><c>true</c> si un token solo-caja puede usar esta ruta y método.</summary>
    public static bool Permitida(PathString path, string method)
    {
        if (path.StartsWithSegments(AuthAdmin, StringComparison.OrdinalIgnoreCase))
            return false;
        if (RutasPermitidas.Any(r => path.StartsWithSegments(r, StringComparison.OrdinalIgnoreCase)))
            return true;
        return path.StartsWithSegments(Branding, StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(method);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && SoloCajaGuard.EsSoloCaja(context.User)
            && !Permitida(context.Request.Path, context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "caja_sin_acceso",
                message = "Su usuario solo puede usar la caja del organismo.",
            }).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
