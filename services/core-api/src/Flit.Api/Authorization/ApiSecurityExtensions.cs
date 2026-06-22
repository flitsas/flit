using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Api.Authorization;

/// <summary>
/// Configura la autenticación JWT y la policy SuperAdmin de Flit.Api (HU #10189, RF01).
///
/// La validación SuperAdmin vive en Flit.Api (no en el Gateway, que relaja JWT en
/// Development). Si no hay llave pública configurada (<c>Jwt:PublicKeyPem</c> o
/// <c>Jwt:PublicKeyPath</c>) se autentica el token sin validar la firma — modo
/// transitorio coherente con el Gateway mientras el login no es obligatorio.
/// </summary>
public static class ApiSecurityExtensions
{
    public static IServiceCollection AddApiSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var jwtSection = configuration.GetSection("Jwt");
        var issuer = jwtSection["Issuer"];
        var audience = jwtSection["Audience"];
        var signingKey = ResolveSigningKey(jwtSection, environment);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // No remapear claims inbound: el claim de rol viaja como "role" y la
                // policy SuperAdmin lo exige vía RoleClaimType="role". Con el mapeo por
                // defecto (true), JWT Bearer renombra "role" al URI largo de .NET y
                // RequireRole nunca encuentra match → todo SuperAdmin recibiría 403.
                options.MapInboundClaims = false;

                if (signingKey is not null)
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
                        ValidIssuer = issuer,
                        ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                        ValidAudience = audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = signingKey,
                        RoleClaimType = AdminAuthorization.RoleClaimType,
                        ClockSkew = TimeSpan.FromSeconds(30),
                    };
                    return;
                }

                // Sin llave de firma: se acepta el token sin validar firma (login no
                // obligatorio aún). El rol SuperAdmin sigue exigiéndose vía policy.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false,
                    ValidateIssuerSigningKey = false,
                    RoleClaimType = AdminAuthorization.RoleClaimType,
                    SignatureValidator = static (token, _) => new JsonWebToken(token),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminAuthorization.SuperAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AdminAuthorization.SuperAdminRole))
            .AddPolicy(AdminAuthorization.OtAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AdminAuthorization.OtAdminRole));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, SuperAdminForbiddenResultHandler>();

        return services;
    }

    private static RsaSecurityKey? ResolveSigningKey(IConfiguration jwtSection, IHostEnvironment environment)
    {
        var pem = jwtSection["PublicKeyPem"];

        if (string.IsNullOrWhiteSpace(pem))
        {
            var path = jwtSection["PublicKeyPath"];
            if (!string.IsNullOrWhiteSpace(path))
            {
                var resolved = Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(environment.ContentRootPath, path);
                if (File.Exists(resolved))
                {
                    pem = File.ReadAllText(resolved);
                }
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
}
