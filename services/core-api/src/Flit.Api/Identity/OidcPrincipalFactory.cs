using System.Security.Claims;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flit.Api.Identity;

/// <summary>
/// Arma la identidad que OpenIddict convierte en tokens (FLIT Suite A-05, HU #12990). El <c>client_id</c> de un
/// producto es su código, así que <c>aud</c> y <c>product</c> son el producto que pidió el login (contrato §2).
/// </summary>
public sealed class OidcPrincipalFactory
{
    /// <summary>Token de usuario para <paramref name="product"/> a partir de la sesión del hub.</summary>
    public Task<ClaimsPrincipal> ForUserAsync(ClaimsPrincipal session, string product, IEnumerable<string> scopes, CancellationToken ct)
    {
        _ = ct;
        var identity = NewIdentity();
        identity.SetClaim(Claims.Subject, session.FindFirstValue(Claims.Subject));
        identity.SetClaim(Claims.Email, session.FindFirstValue(Claims.Email));
        identity.SetClaim(OidcDefaults.TenantIdClaim, session.FindFirstValue(OidcDefaults.TenantIdClaim));
        identity.SetClaim(OidcDefaults.DomainClaim, session.FindFirstValue(OidcDefaults.DomainClaim));
        identity.SetClaim("product", product);
        return Task.FromResult(Finish(identity, product, scopes));
    }

    /// <summary>Token de servicio (contrato §3): <c>sub</c> = cliente, <c>aud</c> = plataforma, los scopes pedidos.</summary>
    public ClaimsPrincipal ForService(string clientId, IEnumerable<string> scopes)
    {
        var identity = NewIdentity();
        identity.SetClaim(Claims.Subject, clientId);
        return Finish(identity, Modules.Security.Application.Products.ProductCodes.Plataforma, scopes);
    }

    private static ClaimsIdentity NewIdentity() =>
        new(OpenIddict.Server.AspNetCore.OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);

    private static ClaimsPrincipal Finish(ClaimsIdentity identity, string audience, IEnumerable<string> scopes)
    {
        identity.SetAudiences(audience);
        identity.SetScopes(scopes);
        // Todo va al access token; el id token solo lleva lo estándar del usuario.
        identity.SetDestinations(claim => claim.Type is Claims.Subject or Claims.Email
            ? [Destinations.AccessToken, Destinations.IdentityToken]
            : [Destinations.AccessToken]);
        return new ClaimsPrincipal(identity);
    }
}
