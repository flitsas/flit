using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Quién puede leer el catálogo de tipos de trámite por gRPC (HU #13731): lo mismo que exige
/// <see cref="CatalogosServicioAuthorization"/> (token de servicio, <c>aud=tramites</c>, scope
/// <c>platform.tramites.catalogos</c>) y, además, que el <c>sub</c> esté en <c>Catalogos:TiposTramite:ClientesPermitidos</c>
/// (hoy solo <c>svc-diagnostico</c>). Sin la lista, nadie entra. Un cliente con el scope que no está en la lista, como
/// <c>svc-comparendos</c>, recibe PERMISSION_DENIED en este método y conserva <c>ListarOrganismosTransito</c>.
/// </summary>
internal static class CatalogoTiposTramiteAuthorization
{
    public const string Policy = "CatalogoTiposTramite";
    public const string ClientesPermitidosKey = "Catalogos:TiposTramite:ClientesPermitidos";

    public static void Configure(AuthorizationPolicyBuilder policy, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(configuration);
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
            {
                var permitidos = configuration.GetSection(ClientesPermitidosKey).Get<string[]>() ?? [];
                return context.User.Identities.Any(identity =>
                    CatalogosServicioAuthorization.EsTokenDeServicio(identity)
                    && identity.FindFirst("sub")?.Value is { } sub
                    && permitidos.Contains(sub, StringComparer.Ordinal));
            });
    }
}
