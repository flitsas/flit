using System.Security.Claims;
using Flit.Platform.Sdk.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Política de las rutas <c>/backoffice/*</c> (plan §5.2, F5b-CF1). El Super Admin pasa desde cualquier compañía, sin rol de
/// producto. Los demás pasan solo si son de la compañía FLIT (<c>tenant_type=FLIT</c>) y tienen <see cref="Permisos.BackofficeVer"/>
/// (un rol armado a mano). Un token de servicio nunca pasa. Cada endpoint puede exigir además su permiso.
/// </summary>
/// <example>
/// <code>
/// var backoffice = api.MapGroup("/backoffice").RequireAuthorization(PoliticaBackoffice.Nombre);
/// </code>
/// </example>
public static class PoliticaBackoffice
{
    public const string Nombre = "impuestos.backoffice";

    public static bool Cumple(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.IsAuthenticated != true || PlatformPrincipal.IsService(user))
            return false;
        return PlatformPrincipal.IsSuperAdmin(user)
            || (UsuarioActual.EsDeFlit(user)
                && user.FindAll("permissions").Any(c => string.Equals(c.Value, Permisos.BackofficeVer, StringComparison.OrdinalIgnoreCase)));
    }

    internal static AuthorizationBuilder AddPoliticaBackoffice(this AuthorizationBuilder builder) =>
        builder.AddPolicy(Nombre, p => p.RequireAuthenticatedUser().RequireAssertion(c => Cumple(c.User)));
}
