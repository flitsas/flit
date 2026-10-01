using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Security;
using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Security.Application.Auth;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flit.Api.Identity;

/// <summary>
/// Servidor OIDC del hub (FLIT Suite A-05, HU #12990) con OpenIddict 7, según la decisión de la espiga A-04
/// (<c>docs/suite/frentes/a-espiga-openiddict.md</c>). Solo se registra con <c>Suite:Oidc:Enabled</c>.
/// </summary>
public static class OidcServerExtensions
{
    /// <summary>Aceptación de tokens (compartida con core-identity) más el servidor OIDC. Epic #13217 (HU #13232).</summary>
    public static IServiceCollection AddFlitOidc(this IServiceCollection services, IConfiguration configuration) =>
        services.AddFlitOidcAcceptance<FlitDbContext>(configuration).AddFlitOidcServer(configuration);

    /// <summary>
    /// El servidor OIDC del hub: sesión del hub (cookie), endpoints de OpenIddict, llaves persistentes, fábrica de
    /// principales y los dos procesos de mantenimiento. Requiere <see cref="OidcAcceptanceExtensions.AddFlitOidcAcceptance{TContext}"/>.
    /// </summary>
    public static IServiceCollection AddFlitOidcServer(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();
        if (!options.Enabled)
            return services;

        // Sesión del hub: la abre POST /connect/login y la lee /connect/authorize. Cookie del host del emisor, sin
        // Domain (contrato §8), cifrada con el keyring de Data Protection que ya comparten las instancias.
        services.AddAuthentication().AddCookie(OidcDefaults.HubSessionScheme, cookie =>
        {
            cookie.Cookie.Name = OidcDefaults.HubSessionCookie;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(options.HubSessionHours);
            cookie.SlidingExpiration = true;
            cookie.LoginPath = options.LoginPath;
            cookie.ReturnUrlParameter = "returnUrl";
            // Detrás del gateway, Request.Host es el host interno de la API: el login se arma sobre el emisor (el host
            // del hub que ve el navegador) y el retorno va relativo, a la misma petición de authorize.
            cookie.Events.OnRedirectToLogin = context =>
            {
                var services = context.HttpContext.RequestServices;
                var issuer = OidcIssuer.Resolve(
                    services.GetRequiredService<IDomainContextAccessor>(),
                    services.GetRequiredService<IProductHosts>(),
                    services.GetRequiredService<IConfiguration>()["Suite:Hosts:Scheme"] ?? "https");
                var request = context.Request;
                var returnUrl = request.PathBase + request.Path + request.QueryString;
                context.Response.Redirect(new Uri(issuer, options.LoginPath.TrimStart('/')) + "?returnUrl=" + Uri.EscapeDataString(returnUrl));
                return Task.CompletedTask;
            };
        });

        services.AddOpenIddict()
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("connect/authorize")
                      .SetTokenEndpointUris("connect/token")
                      .SetEndSessionEndpointUris("connect/logout")
                      .SetConfigurationEndpointUris(".well-known/openid-configuration")
                      .SetJsonWebKeySetEndpointUris(".well-known/jwks.json");

                server.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                server.AllowRefreshTokenFlow();
                server.AllowClientCredentialsFlow();
                server.RegisterScopes([Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, .. OidcDefaults.ServiceScopes]);

                server.SetAccessTokenLifetime(TimeSpan.FromMinutes(options.AccessTokenMinutes));
                server.SetRefreshTokenLifetime(TimeSpan.FromDays(options.RefreshTokenDays));
                server.SetRefreshTokenReuseLeeway(TimeSpan.FromSeconds(options.RefreshTokenReuseLeewaySeconds));

                // El access token es un JWT firmado y legible: los productos lo validan con el JWKS (espiga A-04).
                server.DisableAccessTokenEncryption();

                // HU #13000 (A-09): el refresh token es una referencia opaca (~40 caracteres) y su contenido vive en
                // identity.oidc_tokens. Viaja en la cookie cifrada de sesión de cada producto, que va en cada petición:
                // el JWE completo pesaba ~1,5 KB. Además se revoca en el servidor sin depender de su vencimiento.
                server.UseReferenceRefreshTokens();

                // TLS termina en el borde (nginx); la API recibe HTTP por la red interna.
                server.UseAspNetCore()
                      .EnableAuthorizationEndpointPassthrough()
                      .EnableTokenEndpointPassthrough()
                      .EnableEndSessionEndpointPassthrough()
                      .DisableTransportSecurityRequirement();

                // El emisor sale del host sellado (OidcIssuer). OpenIddict reconoce los endpoints comparando la URL
                // pedida contra la base: se reescriben LAS DOS, o el descubrimiento da 404 (espiga A-04).
                // HU #12993 (A-08): retornos a los dominios activos de la red que atiende la petición.
                OidcNetworkRedirects.Register(server);

                server.AddEventHandler<OpenIddictServerEvents.ProcessRequestContext>(handler => handler
                    .UseInlineHandler(context =>
                    {
                        var http = context.Transaction.GetHttpRequest()?.HttpContext;
                        if (http is null || context.RequestUri is not { } requested)
                            return default;

                        var hosts = http.RequestServices.GetRequiredService<IProductHosts>();
                        var domain = http.RequestServices.GetRequiredService<IDomainContextAccessor>();
                        var scheme = http.RequestServices.GetRequiredService<IConfiguration>()["Suite:Hosts:Scheme"] ?? "https";
                        var issuer = OidcIssuer.Resolve(domain, hosts, scheme);
                        context.BaseUri = issuer;
                        context.RequestUri = new Uri(issuer, requested.PathAndQuery.TrimStart('/'));
                        return default;
                    })
                    .SetOrder(OpenIddictServerAspNetCoreHandlers.ResolveRequestUri.Descriptor.Order + 1));
            });

        // Llaves de firma y cifrado persistentes (security.jwt_signing_keys, cifradas con Data Protection): las mismas
        // en todas las instancias y en cada reinicio. Rotar = cambiar Suite:Oidc:SigningKeyId.
        services.AddOptions<OpenIddictServerOptions>().Configure<IServiceProvider, IOptions<OidcOptions>>((server, sp, oidc) =>
        {
            var signing = new RsaSecurityKey(PersistentJwtSigningKeyStore.LoadOrCreate(sp, oidc.Value.SigningKeyId)) { KeyId = oidc.Value.SigningKeyId };
            var encryption = new RsaSecurityKey(PersistentJwtSigningKeyStore.LoadOrCreate(sp, oidc.Value.EncryptionKeyId)) { KeyId = oidc.Value.EncryptionKeyId };
            server.SigningCredentials.Add(new SigningCredentials(signing, SecurityAlgorithms.RsaSha256));
            server.EncryptionCredentials.Add(new EncryptingCredentials(encryption, SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
        });

        services.AddScoped<OidcPrincipalFactory>();
        services.AddHostedService<OidcClientSync>();
        services.AddHostedService<OidcPruningService>();
        return services;
    }
}
