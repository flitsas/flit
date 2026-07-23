using Flit.Ict.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Ict.Api.Authorization;

/// <summary>
/// Autenticación/autorización del token propio de ICT (aud=flit-ict). Aislado del token de
/// plataforma: issuer/audiencia/llave RSA distintos. Debe registrarse DESPUÉS de AddIctInfrastructure
/// (usa el IctJwtKeyMaterial singleton).
/// </summary>
public static class IctSecurityExtensions
{
    public const string IctClientPolicy = "IctClient";

    public static IServiceCollection AddIctApiSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

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
                .RequireClaim("scope", "ict.transactions.write"));

        return services;
    }
}
