using Flit.Admin.Domain.Companies.Domains;
using Flit.Infrastructure.Security;
using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Security.Application.Products;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
                tvp.IssuerValidator = (issuer, _, _) =>
                    string.Equals(issuer, legacyIssuer, StringComparison.Ordinal) || registry.IsValid(issuer)
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
        var issuers = new List<string> { hosts.UrlFor(ProductCodes.Plataforma).TrimEnd('/') + "/" };
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
