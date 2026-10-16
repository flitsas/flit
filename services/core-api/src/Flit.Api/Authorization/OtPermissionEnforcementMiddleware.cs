using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Flit.Api.Authorization;

/// <summary>Opciones de <c>Security:OtPermissionEnforcement</c> (HU #13639, decisión P2 del Anexo A).</summary>
public sealed class OtPermissionEnforcementOptions
{
    public const string Section = "Security:OtPermissionEnforcement";

    /// <summary>
    /// Apagado por defecto: las rutas OT solo exigen su policy de rol, como hoy. Encendido, cada ruta de
    /// <see cref="OtPermissionMap"/> exige además su slug OT en el claim <c>permissions</c>. Encender al menos 12 h
    /// después del despliegue que asigna los slugs (los permisos viajan en el JWT).
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Exige el slug OT de <see cref="OtPermissionMap"/> en AND con la policy del grupo (patrón de
/// <c>consolidado-masivo.download</c>). Va después de <c>UseAuthorization</c>: el 401 y el 403 de rol ganan; este
/// responde 403 <c>OT_PERMISSION_REQUIRED</c>. Reutiliza <see cref="PermissionAuthorizationHandler"/> (bypass SuperAdmin).
/// </summary>
public sealed class OtPermissionEnforcementMiddleware(RequestDelegate next, IOptionsMonitor<OtPermissionEnforcementOptions> options)
{
    public async Task InvokeAsync(HttpContext context, IAuthorizationService authorization)
    {
        if (options.CurrentValue.Enabled
            && context.User.Identity?.IsAuthenticated == true
            && context.GetEndpoint() is RouteEndpoint endpoint
            && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>() is { } methods
            && TryGetSlug(endpoint, methods, out var slug))
        {
            var result = await authorization.AuthorizeAsync(context.User, null, [new PermissionRequirement(slug)]).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "OT_PERMISSION_REQUIRED",
                    message = "No tienes el permiso para usar este módulo del organismo de tránsito.",
                    permission = slug,
                }).ConfigureAwait(false);
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool TryGetSlug(RouteEndpoint endpoint, IHttpMethodMetadata methods, out string slug)
    {
        foreach (var method in methods.HttpMethods)
        {
            if (OtPermissionMap.BySignature.TryGetValue(OtPermissionMap.Signature(method, endpoint.RoutePattern.RawText), out slug!))
            {
                return true;
            }
        }

        slug = string.Empty;
        return false;
    }
}
