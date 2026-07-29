using System.Security.Cryptography;
using System.Text;
using Flit.Ict.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Ict.Api.Authorization;

/// <summary>
/// Autenticación/autorización de core-ict. Registra DOS esquemas aislados:
/// (1) el token propio de ICT del cliente gestor (aud=flit-ict, llave RSA) para las rutas de negocio, y
/// (2) el SERVICE-TOKEN del canal inverso core-api → core-ict (aud=core-ict-internal, HMAC compartido)
/// que protege el gRPC de callback de estados. Ambos aislados del token de plataforma.
/// Debe registrarse DESPUÉS de AddIctInfrastructure (usa el IctJwtKeyMaterial singleton).
/// </summary>
public static class IctSecurityExtensions
{
    public const string IctClientPolicy = "IctClient";

    /// <summary>Esquema/policy del service-token del canal inverso (core-api → core-ict). Scope ict.state.</summary>
    public const string CoreApiCallbackScheme = "CoreApiCallback";
    public const string CoreApiCallbackPolicy = "CoreApiCallback";

    public static IServiceCollection AddIctApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Secreto HMAC compartido con core-api (mismo del canal directo). Sin secreto → llave aleatoria =
        // fail-closed (ningún token real valida). El secreto real llega por Ict__ServiceToken__Secret.
        var callbackSecret = configuration["Ict:StateCallback:Secret"] ?? configuration["Ict:ServiceToken:Secret"];
        var callbackKey = string.IsNullOrWhiteSpace(callbackSecret)
            ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(48))
            : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(callbackSecret));
        var callbackIssuer = configuration["Ict:StateCallback:Issuer"] ?? "flit-api-svc";
        var callbackAudience = configuration["Ict:StateCallback:Audience"] ?? "core-ict-internal";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddJwtBearer(CoreApiCallbackScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = callbackIssuer,
                    ValidateAudience = true,
                    ValidAudience = callbackAudience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = callbackKey,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IctJwtKeyMaterial>((options, keyMaterial) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = keyMaterial.Issuer,
                    ValidateAudience = true,
                    ValidAudience = keyMaterial.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = keyMaterial.SigningKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(IctClientPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("scope", "ict.transactions.write"))
            // Canal inverso: exige el service-token (esquema CoreApiCallback) + scope ict.state.
            .AddPolicy(CoreApiCallbackPolicy, policy => policy
                .AddAuthenticationSchemes(CoreApiCallbackScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("scope", "ict.state"));

        return services;
    }
}
