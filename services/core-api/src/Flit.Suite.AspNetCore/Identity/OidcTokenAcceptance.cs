using Microsoft.Extensions.Configuration;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Infrastructure.Security;
using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Security.Application.Products;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flit.Api.Identity;

/// <summary>
/// HU #12992 (FLIT Suite A-07) — core-api acepta, además de su JWT de siempre, los tokens del servidor OIDC del hub:
/// firmados con la llave OIDC, de un emisor de <c>/platform/issuers</c> (hub del ambiente o dominio de red activo,
/// caché de 5 minutos, contrato §2) y con <c>aud</c> de un producto que core-api sirve (plataforma o Trámites).
/// Solo aplica cuando la API valida tokens (llave pública o <c>Jwt:ValidateIssuedTokens</c>); en el modo permisivo
/// anterior a A-03 no hay nada que ampliar.
/// </summary>
internal static class OidcTokenAcceptance
{
    /// <summary>Productos cuyas rutas atiende core-api.</summary>
    private static readonly string[] ServedAudiences = [ProductCodes.Plataforma, ProductCodes.Tramites];

    public static void Register(IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<OidcIssuerRegistry>();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<IServiceProvider, IOptions<OidcOptions>, OidcIssuerRegistry>((options, sp, oidc, registry) =>
            {
                var tvp = options.TokenValidationParameters;
                if (!tvp.ValidateIssuerSigningKey)
                    return;

                var oidcKey = PersistentJwtSigningKeyStore.LoadOrCreate(sp, oidc.Value.SigningKeyId);
                var oidcPublic = new RsaSecurityKey(oidcKey.ExportParameters(includePrivateParameters: false)) { KeyId = oidc.Value.SigningKeyId };
                tvp.IssuerSigningKeys = [.. tvp.IssuerSigningKeys ?? [], .. tvp.IssuerSigningKey is { } legacy ? [legacy] : Array.Empty<SecurityKey>(), oidcPublic];

                var legacyIssuer = tvp.ValidIssuer;
                var externalIssuer = sp.GetRequiredService<IConfiguration>()["ExternalJwt:Issuer"] ?? Flit.Api.Authorization.TokenValidationExtensions.DefaultExternalIssuer;
                // HU #13087 AC5 — el pase de los clientes externos nunca autentica en la plataforma.
                tvp.IssuerValidator = (issuer, _, _) =>
                    !string.Equals(issuer, externalIssuer, StringComparison.Ordinal)
                    && (string.Equals(issuer, legacyIssuer, StringComparison.Ordinal) || registry.IsValid(issuer))
                        ? issuer
                        : throw new SecurityTokenInvalidIssuerException($"Emisor no aceptado: {issuer}") { InvalidIssuer = issuer };

                tvp.ValidAudiences = [.. tvp.ValidAudiences ?? [], .. tvp.ValidAudience is { } aud ? [aud] : Array.Empty<string>(), .. ServedAudiences];
            });
    }
}

/// <summary>Emisores válidos (contrato §2 y §6), con la misma fuente que <c>GET /api/v1/platform/issuers</c>.</summary>
internal sealed class OidcIssuerRegistry(IServiceProvider services, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const string CacheKey = "oidc:issuers";

    public bool IsValid(string issuer) => Current().Contains(issuer);

    public IReadOnlySet<string> Current() =>
        cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return Load();
        })!;

    public static async Task<IReadOnlyList<string>> ListAsync(IProductHosts hosts, ITenantDomainRepository domains, string scheme, CancellationToken ct)
    {
        // El hub de la raíz principal y el de cada raíz alternativa (Suite:Hosts:AlternateRoots): cada uno es emisor.
        var issuers = hosts.AllUrlsFor(ProductCodes.Plataforma).Select(url => url.TrimEnd('/') + "/").ToList();
        foreach (var host in await domains.ListActiveHostsAsync(ct).ConfigureAwait(false))
            issuers.Add($"{scheme}://{host.Trim().ToLowerInvariant()}/");
        return issuers.Distinct(StringComparer.Ordinal).ToArray();
    }

    private HashSet<string> Load()
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var scheme = provider.GetRequiredService<IConfiguration>()["Suite:Hosts:Scheme"] ?? "https";
        var list = ListAsync(provider.GetRequiredService<IProductHosts>(), provider.GetRequiredService<ITenantDomainRepository>(), scheme, CancellationToken.None)
            .GetAwaiter().GetResult();
        return new HashSet<string>(list, StringComparer.Ordinal);
    }
}

/// <summary>
/// Cierre de sesión en toda la suite (simulador de la suite, ejemplo 2): un token del servidor OIDC lleva la autorización
/// de la sesión del hub (<c>oi_au_id</c>). Al cerrar sesión (A-13) esa autorización se revoca; desde entonces sus
/// tokens de acceso se rechazan aunque no hayan vencido, así los productos y el hub abiertos en este navegador pierden
/// la sesión en su siguiente llamada y no hasta 15 minutos después. La consulta se guarda 15 segundos por autorización.
/// </summary>
internal static class OidcSessionCheck
{
    private const string AuthorizationIdClaim = "oi_au_id";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var id = context.Principal?.FindFirst(AuthorizationIdClaim)?.Value;
        if (string.IsNullOrEmpty(id))
            return; // JWT de siempre, token de servicio o servidor OIDC apagado.

        var services = context.HttpContext.RequestServices;
        if (services.GetService<IOpenIddictAuthorizationManager>() is not { } authorizations)
            return;

        var ct = context.HttpContext.RequestAborted;
        var valid = await services.GetRequiredService<IMemoryCache>().GetOrCreateAsync($"oidc:authz:{id}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            var authorization = await authorizations.FindByIdAsync(id, ct).ConfigureAwait(false);
            return authorization is not null && await authorizations.HasStatusAsync(authorization, Statuses.Valid, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (!valid)
            context.Fail(new SecurityTokenException("La sesión de la suite se cerró."));
    }
}
