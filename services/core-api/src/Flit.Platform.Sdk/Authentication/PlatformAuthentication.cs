using Flit.Platform.Sdk.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Platform.Sdk.Authentication;

/// <summary>Configuración de <see cref="PlatformAuthenticationExtensions.AddFlitPlatformAuthentication"/> (sección <c>Platform:Auth</c>).</summary>
public sealed class PlatformAuthOptions
{
    public const string SectionName = "Platform:Auth";

    /// <summary>Código de este servicio: el <c>aud</c> que deben traer los tokens que recibe (contrato v1.3 §3).</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Emisores aceptados: el hub del ambiente (p. ej. <c>https://&lt;ambiente&gt;.flitsas.online/</c>).</summary>
    public IList<string> Issuers { get; } = [];

    /// <summary>JWKS de Identidad por la red interna (p. ej. <c>http://core-identity:4025/.well-known/jwks.json</c>).</summary>
    public string JwksUri { get; set; } = string.Empty;
}

/// <summary>
/// Validación de tokens de la plataforma para un servicio que NO tiene la base de Identidad (Epic #13316, HU #13336):
/// firma contra el JWKS publicado por Identidad (en caché; se vuelve a leer si llega una llave desconocida), emisor de
/// la lista y audiencia del propio servicio. core-api y core-identity siguen con su validación de siempre (leen las
/// llaves de su base) en Flit.Suite.AspNetCore.
/// </summary>
public static class PlatformAuthenticationExtensions
{
    public const string JwksHttpClientName = "flit-platform-jwks";

    public static IServiceCollection AddFlitPlatformAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new PlatformAuthOptions();
        configuration.GetSection(PlatformAuthOptions.SectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.Audience) || options.Issuers.Count == 0 || !Uri.TryCreate(options.JwksUri, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"{PlatformAuthOptions.SectionName} necesita Audience, al menos un Issuers y JwksUri absoluto.");
        }

        services.AddHttpClient(JwksHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpContextAccessor();
        services.TryAddScoped<IPlatformTenantAccessor, HttpPlatformTenantAccessor>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IHttpClientFactory>((jwt, httpClients) =>
            {
                jwt.MapInboundClaims = false;
                jwt.RequireHttpsMetadata = false; // red interna; la firma se valida igual
                jwt.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    options.JwksUri,
                    new JwksConfigurationRetriever(),
                    new HttpDocumentRetriever(httpClients.CreateClient(JwksHttpClientName)) { RequireHttps = false })
                {
                    AutomaticRefreshInterval = TimeSpan.FromMinutes(10),
                    RefreshInterval = TimeSpan.FromSeconds(30),
                };
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = [.. options.Issuers],
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
            });
        return services;
    }

    /// <summary>Lee un JWKS (no un documento de descubrimiento) como configuración con sus llaves de firma.</summary>
    internal sealed class JwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            ArgumentNullException.ThrowIfNull(retriever);
            var json = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);
            var keys = new JsonWebKeySet(json);
            var configuration = new OpenIdConnectConfiguration { JsonWebKeySet = keys, JwksUri = address };
            foreach (var key in keys.GetSigningKeys())
                configuration.SigningKeys.Add(key);
            return configuration;
        }
    }
}
