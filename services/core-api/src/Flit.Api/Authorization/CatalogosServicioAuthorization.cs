using System.Security.Claims;
using Flit.Api.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Quién puede leer los catálogos de la plataforma por gRPC (HU #13459): un cliente de servicio de Identidad
/// (<c>sub</c> con <c>svc-</c>), con <c>aud=tramites</c> y el scope <c>platform.tramites.catalogos</c> (contrato v1.3 §3).
/// Hoy solo <c>svc-comparendos</c>, para su réplica de organismos de tránsito.
/// </summary>
internal static class CatalogosServicioAuthorization
{
    public const string Scope = "platform.tramites.catalogos";

    public static void Configure(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identities.Any(EsTokenDeServicio));
    }

    internal static bool EsTokenDeServicio(ClaimsIdentity identity) =>
        identity.IsAuthenticated
        && identity.FindFirst("sub")?.Value is { } sub
        && sub.StartsWith(OidcDefaults.ServiceClientPrefix, StringComparison.Ordinal)
        && identity.HasClaim("aud", ServiceAudiences.Tramites)
        && identity.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(Scope, StringComparer.Ordinal);
}
