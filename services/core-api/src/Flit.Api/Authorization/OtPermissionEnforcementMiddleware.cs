using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Flit.Api.Authorization;

/// <summary>Opciones de <c>Security:OtPermissionEnforcement</c> (HU #13639, decisión P2 del Anexo A).</summary>
public sealed class OtPermissionEnforcementOptions
{
    public const string Section = "Security:OtPermissionEnforcement";

    /// <summary>
    /// Encendido por defecto (decisión del usuario, 2026-10-21): cada ruta de <see cref="OtPermissionMap"/> exige además
    /// su slug OT en el claim <c>permissions</c>. Apagarlo es el freno de emergencia: las rutas OT vuelven a exigir solo su
    /// policy de rol. En el primer despliegue hay que cerrar las sesiones abiertas (los permisos viajan en el JWT).
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Exige el slug OT de <see cref="OtPermissionMap"/> en AND con la policy del grupo (patrón de
/// <c>consolidado-masivo.download</c>). Va después de <c>UseAuthorization</c>: el 401 y el 403 de rol ganan; este
/// responde 403 <c>OT_PERMISSION_REQUIRED</c>. Reutiliza <see cref="PermissionAuthorizationHandler"/> (bypass SuperAdmin).
/// <para>
/// HU #13655: con el flag apagado, Usuarios (<c>ot.usuarios.manage</c>) e Integración (<c>ot.integracion.manage</c>)
/// se exigen igual a quien no es <c>ot_admin</c> ni SuperAdmin. Son del Admin OT (decisión P1 del Anexo A) y, desde que
/// existe el rol «Gestor OT», un usuario del organismo que no es administrador ya no debe poder invitar usuarios (ni
/// administradores) ni crear webhooks solo por pertenecer al organismo. El Admin OT se los delega por usuario.
/// </para>
/// </summary>
public sealed class OtPermissionEnforcementMiddleware(RequestDelegate next, IOptionsMonitor<OtPermissionEnforcementOptions> options)
{
    public async Task InvokeAsync(HttpContext context, IAuthorizationService authorization)
    {
        var enabled = options.CurrentValue.Enabled;
        if (context.User.Identity?.IsAuthenticated == true
            && (enabled || !EsAdministradorOt(context.User))
            && context.GetEndpoint() is RouteEndpoint endpoint
            && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>() is { } methods
            && TryGetSlug(endpoint, methods, out var slug)
            && (enabled || slug is OtPermissionMap.Usuarios or OtPermissionMap.Integracion))
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

    /// <summary>SuperAdmin u ot_admin: con el flag apagado no se les exige ningún slug (HU #13655).</summary>
    private static bool EsAdministradorOt(System.Security.Claims.ClaimsPrincipal user) =>
        user.Claims.Any(c => c.Type == AdminAuthorization.RoleClaimType
            && (c.Value == AdminAuthorization.SuperAdminRole || c.Value == AdminAuthorization.OtAdminRole));

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
