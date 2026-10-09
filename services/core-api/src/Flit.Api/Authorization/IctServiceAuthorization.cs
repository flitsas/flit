using System.Security.Claims;
using Flit.Api.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Quién puede llamar al gRPC de ICT en core-api (orquestación y consultas). Epic #13316, HU #13335: ICT deja el secreto
/// HMAC compartido y pasa al token de servicio de Identidad (cliente <c>svc-ict</c>, scope
/// <c>platform.tramites.ict</c>, <c>aud=tramites</c>, contrato v1.3 §3). La transición va con dos banderas de
/// <c>Ict:ServiceToken</c>, sin cambiar nada mientras no se toquen:
/// <list type="bullet">
///   <item><c>AcceptLegacy</c> (por defecto <c>true</c>): el JWT HMAC firmado con <c>Ict:ServiceToken:Secret</c>.
///   Apagarla es el corte: desde ahí ese token se rechaza.</item>
///   <item><c>AcceptIdentity</c> (por defecto <c>false</c>): el token de servicio emitido por Identidad, que valida el
///   esquema de la plataforma (firma OIDC, emisor de la suite, audiencia).</item>
/// </list>
/// Cada token solo puede pasar por su propio esquema (llaves y emisores distintos), así que mirar los claims de la
/// identidad que sí autenticó es suficiente.
/// </summary>
internal static class IctServiceAuthorization
{
    public const string LegacyScope = "ict.orchestration";
    public const string IdentityScope = "platform.tramites.ict";

    public static void Configure(AuthorizationPolicyBuilder policy, IConfiguration configuration, string legacyIssuer)
    {
        var acceptLegacy = configuration.GetValue("Ict:ServiceToken:AcceptLegacy", true);
        var acceptIdentity = configuration.GetValue("Ict:ServiceToken:AcceptIdentity", false);

        policy.AddAuthenticationSchemes(ApiSecurityExtensions.IctServiceScheme);
        if (acceptIdentity)
            policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);

        policy.RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identities.Any(identity =>
                (acceptLegacy && IsLegacyToken(identity, legacyIssuer)) || (acceptIdentity && IsIdentityServiceToken(identity))));
    }

    internal static bool IsLegacyToken(ClaimsIdentity identity, string legacyIssuer) =>
        identity.IsAuthenticated
        && identity.HasClaim("iss", legacyIssuer)
        && HasScope(identity, LegacyScope);

    internal static bool IsIdentityServiceToken(ClaimsIdentity identity) =>
        identity.IsAuthenticated
        && identity.FindFirst("sub")?.Value is { } sub
        && sub.StartsWith(OidcDefaults.ServiceClientPrefix, StringComparison.Ordinal)
        && identity.HasClaim("aud", ServiceAudiences.Tramites)
        && HasScope(identity, IdentityScope);

    private static bool HasScope(ClaimsIdentity identity, string scope) =>
        identity.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
