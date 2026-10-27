using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Ict.Api.Authorization;

/// <summary>
/// Validación del JWT de PLATAFORMA (el del login de FLIT, reenviado por el Gateway) para los submódulos ICT de
/// plataforma (Logs, Trazabilidad, administración de clientes). Misma regla que core-api: firma RS256 con la llave
/// pública de plataforma, emisor, audiencia y vencimiento (ClockSkew 30 s). Esquema propio, aislado del token ICT del
/// cliente gestor y del service-token del canal inverso.
/// </summary>
/// <remarks>
/// Configuración (mismos nombres que core-api, sección <c>Jwt</c>):
/// <c>Jwt:Issuer</c> (def. https://api.flit.co), <c>Jwt:Audience</c> (def. flit-api),
/// <c>Jwt:PublicKeyPem</c> | <c>Jwt:PublicKeyPath</c> | <c>Jwt:JwksUri</c> (llave pública), y
/// <c>Jwt:ValidateIssuedTokens</c> (def. true fuera de Development, false en Development).
/// Fuera de Development no se puede apagar y sin llave el arranque falla (fail-fast): nunca se aceptan tokens sin firma.
/// </remarks>
public static class PlatformTokenAuthentication
{
    public const string Scheme = "PlatformToken";

    private const string PrincipalItemKey = "flit.ict.platform-principal";

    public static IServiceCollection AddIctPlatformTokenValidation(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var jwt = configuration.GetSection("Jwt");
        var issuer = jwt["Issuer"] is { Length: > 0 } i ? i : "https://api.flit.co";
        var audience = jwt["Audience"] is { Length: > 0 } a ? a : "flit-api";
        var jwksUri = jwt["JwksUri"];
        var key = ResolvePublicKey(jwt, environment);

        var validate = jwt.GetValue<bool?>("ValidateIssuedTokens") ?? !environment.IsDevelopment();
        if (!validate && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "core-ict: Jwt:ValidateIssuedTokens=false solo se permite en Development; fuera de Development el token de plataforma siempre se valida.");
        }

        var hasJwks = Uri.TryCreate(jwksUri, UriKind.Absolute, out var jwksAddress);
        if (validate && key is null && !hasJwks)
        {
            throw new InvalidOperationException(
                "core-ict: falta la llave pública de plataforma para validar el token. Configura Jwt__PublicKeyPem, Jwt__PublicKeyPath o Jwt__JwksUri "
                + "(p. ej. http://gateway:4002/.well-known/jwks.json), o Jwt__ValidateIssuedTokens=false solo en Development.");
        }

        services.AddAuthentication().AddJwtBearer(Scheme, options =>
        {
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = false; // el JWKS va por la red interna; la firma se valida igual

            if (!validate)
            {
                // Solo Development: sin validar (comportamiento previo, para ambientes locales sin llave).
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false,
                    ValidateIssuerSigningKey = false,
                    RoleClaimType = "role",
                    SignatureValidator = static (token, _) => new JsonWebToken(token),
                };
                return;
            }

            if (key is null)
            {
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    jwksAddress!.ToString(),
                    new JwksConfigurationRetriever(),
                    new HttpDocumentRetriever { RequireHttps = false })
                {
                    AutomaticRefreshInterval = TimeSpan.FromMinutes(10),
                    RefreshInterval = TimeSpan.FromSeconds(30),
                };
            }

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                RoleClaimType = "role",
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });

        return services;
    }

    /// <summary>
    /// Autentica el Bearer contra el esquema de plataforma y deja el principal validado para
    /// <see cref="PlatformAccessReader.Read"/>. Token ausente o inválido → no deja nada (sin acceso).
    /// </summary>
    public static async Task AuthenticateAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items.Remove(PrincipalItemKey);

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var result = await context.AuthenticateAsync(Scheme);
        if (result.Succeeded && result.Principal is { } principal)
        {
            context.Items[PrincipalItemKey] = principal;
        }
    }

    /// <summary>Principal de plataforma ya validado, o <c>null</c>.</summary>
    internal static ClaimsPrincipal? GetValidatedPrincipal(HttpContext context) =>
        context.Items.TryGetValue(PrincipalItemKey, out var p) ? p as ClaimsPrincipal : null;

    /// <summary>Middleware: tras <c>UseAuthentication</c>, valida el token de plataforma de cada petición con Bearer.</summary>
    public static IApplicationBuilder UseIctPlatformToken(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await AuthenticateAsync(context);
            await next();
        });

    private static RsaSecurityKey? ResolvePublicKey(IConfiguration jwt, IHostEnvironment environment)
    {
        var pem = jwt["PublicKeyPem"];
        if (string.IsNullOrWhiteSpace(pem) && jwt["PublicKeyPath"] is { Length: > 0 } path)
        {
            var resolved = Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path);
            if (File.Exists(resolved))
            {
                pem = File.ReadAllText(resolved);
            }
        }

        if (string.IsNullOrWhiteSpace(pem))
        {
            return null;
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa);
    }

    /// <summary>Lee un JWKS (no un documento de descubrimiento) como configuración con sus llaves de firma.</summary>
    private sealed class JwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            var json = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);
            var keys = new JsonWebKeySet(json);
            var configuration = new OpenIdConnectConfiguration { JsonWebKeySet = keys, JwksUri = address };
            foreach (var k in keys.GetSigningKeys())
            {
                configuration.SigningKeys.Add(k);
            }

            return configuration;
        }
    }
}
