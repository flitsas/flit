using System.Security.Claims;
using System.Text.Json;
using Flit.Modules.Security.Application.Products;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flit.Api.Identity;

/// <summary>Identidad para un token, o el código de por qué no se emite (contrato §10).</summary>
public sealed record OidcGrant(ClaimsPrincipal? Principal, string? DeniedCode)
{
    public static OidcGrant Denied(string code) => new(null, code);
}

/// <summary>
/// Arma la identidad que OpenIddict convierte en tokens. El <c>client_id</c> de un producto es su código, así que
/// <c>aud</c> y <c>product</c> son el producto que pidió el login.
/// <para>
/// HU #12992 (FLIT Suite A-07) — token por producto según el contrato §2: se relee el usuario de la base en CADA
/// emisión, también en cada refresh, así un cambio de roles, una suspensión o un producto apagado se aplican a más
/// tardar en el siguiente refresh (15 minutos). Roles y permisos son solo los del producto (<see cref="IProductAccessResolver"/>);
/// el SuperAdmin entra a cualquier producto con <c>SuperAdmin</c> en <c>roles</c>, <c>role</c> y <c>role_code</c> (§2.1).
/// Se conservan los nombres y formatos de claims del JWT de siempre (<c>RsaJwtTokenIssuer</c>).
/// </para>
/// </summary>
public sealed class OidcPrincipalFactory(IAuthUserRepository users, IProductAccessResolver access)
{
    private const string SuperAdmin = "SuperAdmin";

    public const string ProductNotEnabled = "PRODUCT_NOT_ENABLED";
    public const string ProductRoleRequired = "PRODUCT_ROLE_REQUIRED";
    public const string SessionInvalid = "SESSION_INVALID";

    /// <summary>
    /// Token de usuario para <paramref name="product"/>. <paramref name="source"/> es la sesión del hub (authorize) o
    /// la identidad del refresh token: de ella solo se toman el usuario y el dominio de la sesión.
    /// </summary>
    public async Task<OidcGrant> ForUserAsync(ClaimsPrincipal source, string product, IEnumerable<string> scopes, CancellationToken ct)
    {
        var email = source.FindFirstValue(Claims.Email);
        var userId = source.FindFirstValue(Claims.Subject);
        var user = string.IsNullOrWhiteSpace(email) ? null : await users.FindByEmailAsync(email, ct).ConfigureAwait(false);

        // La cuenta cambió desde que se abrió la sesión: borrada, desactivada, suspendida o sin roles activos.
        if (user is null
            || !string.Equals(user.UserId.ToString(), userId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase)
            || user.IsTemporarilySuspended
            || (user.TotalAssignedRolesCount > 0 && user.ActiveRoles.Count == 0))
        {
            return OidcGrant.Denied(SessionInvalid);
        }

        var isSuperAdmin = user.ActiveRoles.Any(r => string.Equals(r.Code, SuperAdmin, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<RoleRef> roles;
        IReadOnlyList<string> permissions;
        if (isSuperAdmin)
        {
            // §2.1: bypass en todos los productos, aunque no esté habilitado ni tenga rol en él.
            roles = user.ActiveRoles.Select(r => new RoleRef(r.Id, r.Code)).ToList();
            permissions = user.PermissionSlugs;
        }
        else
        {
            var grant = await access.ResolveAsync(user.UserId, user.TenantId, product, ct).ConfigureAwait(false);
            if (!grant.ProductEnabled)
                return OidcGrant.Denied(ProductNotEnabled);

            // El hub (plataforma) no exige rol: todo usuario entra a ver sus productos. Los demás productos sí.
            if (grant.Roles.Count == 0 && product != ProductCodes.Plataforma)
                return OidcGrant.Denied(ProductRoleRequired);

            roles = grant.Roles;
            permissions = grant.Permissions;
        }

        var identity = NewIdentity();
        identity.SetClaim(Claims.Subject, user.UserId.ToString());
        identity.SetClaim(Claims.Email, user.Email);
        identity.SetClaim("tenant_id", user.TenantId.ToString());
        identity.SetClaim("tenant_name", user.TenantName);
        identity.SetClaim("company_name", user.TenantName);
        identity.SetClaim("company_nit", user.TenantTaxId);
        identity.SetClaim("entity_type", user.EntityType);
        identity.SetClaim("tenant_type", user.TenantType);
        identity.AddClaim(new Claim("is_group_parent", user.IsGroupParent ? "true" : "false", JsonClaimValueTypes.Json));
        identity.SetClaim("dom", source.FindFirstValue(OidcDefaults.DomainClaim) ?? "flit");
        identity.SetClaim("product", product);

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim("role_id", role.Id.ToString()));
            identity.AddClaim(new Claim("role_code", role.Code));
            identity.AddClaim(new Claim(Claims.Role, role.Code));
        }

        // Arrays explícitos, aunque tengan un solo elemento o ninguno (el frontend espera siempre un array).
        identity.AddClaim(new Claim("roles", JsonSerializer.Serialize(roles.Select(r => new { id = r.Id, code = r.Code })), JsonClaimValueTypes.JsonArray));
        identity.AddClaim(new Claim("permissions", JsonSerializer.Serialize(permissions), JsonClaimValueTypes.JsonArray));

        return new OidcGrant(Finish(identity, product, scopes), null);
    }

    /// <summary>Token de servicio (contrato §3): <c>sub</c> = cliente, <c>aud</c> = plataforma, los scopes pedidos.</summary>
    public ClaimsPrincipal ForService(string clientId, IEnumerable<string> scopes)
    {
        var identity = NewIdentity();
        identity.SetClaim(Claims.Subject, clientId);
        return Finish(identity, ProductCodes.Plataforma, scopes);
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
