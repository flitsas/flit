using System.Security.Claims;
using Flit.Platform.Sdk.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Política de las rutas <c>/backoffice/*</c> (HU #13728, D-16, ADR-0073). Exige a la vez el permiso
/// <see cref="Permisos.BackofficeVer"/> y el claim <c>tenant_type=FLIT</c> del access token. Un usuario con el rol
/// <c>backoffice_diagnostico</c> en una compañía que no es FLIT, o de la compañía FLIT sin el permiso, recibe 403. El
/// SuperAdmin cuenta como si tuviera el permiso (igual que en <see cref="Capacidades"/>), pero también necesita ser de la
/// compañía FLIT. Un token de servicio nunca pasa: los servicios entran por gRPC.
/// </summary>
public static class PoliticaBackoffice
{
    public const string Nombre = "diagnostico.backoffice";

    /// <summary>El usuario puede usar el backoffice: no es un servicio, es de la compañía FLIT y tiene el permiso.</summary>
    public static bool Cumple(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.IsAuthenticated != true || PlatformPrincipal.IsService(user))
            return false;

        var esFlit = string.Equals(user.FindFirstValue("tenant_type"), UsuarioActual.TipoEmpresaFlit, StringComparison.OrdinalIgnoreCase);
        var tienePermiso = PlatformPrincipal.IsSuperAdmin(user)
            || user.FindAll("permissions").Any(c => string.Equals(c.Value, Permisos.BackofficeVer, StringComparison.OrdinalIgnoreCase));
        return esFlit && tienePermiso;
    }

    internal static AuthorizationBuilder AddPoliticaBackoffice(this AuthorizationBuilder builder) =>
        builder.AddPolicy(Nombre, p => p.RequireAuthenticatedUser().RequireAssertion(c => Cumple(c.User)));
}
