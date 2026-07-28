using System.Security.Cryptography;
using System.Text;
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
    /// <summary>Esquema/policy del SERVICE-TOKEN gRPC este-oeste (core-ict → core-api). Aislado del token de plataforma.</summary>
    public const string IctServiceScheme = "IctService";
    public const string IctServicePolicy = "IctService";

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

        // Service-token gRPC este-oeste (ICT): esquema JwtBearer APARTE con secreto compartido (HMAC),
        // aislado del token de plataforma. Solo lo consume la policy IctServicePolicy en los gRPC services
        // (IctOrchestration/IctConsultation). Sin secreto configurado → llave aleatoria = fail-closed
        // (ningún token real valida). El secreto real llega por Ict__ServiceToken__Secret (env/appsettings).
        var svcSecret = configuration["Ict:ServiceToken:Secret"];
        var svcIssuer = configuration["Ict:ServiceToken:Issuer"] ?? "flit-ict-svc";
        var svcAudience = configuration["Ict:ServiceToken:Audience"] ?? "flit-internal";
        var svcKey = string.IsNullOrWhiteSpace(svcSecret)
            ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(48))
            : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(svcSecret));

        services.AddAuthentication().AddJwtBearer(IctServiceScheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = svcIssuer,
                ValidateAudience = true,
                ValidAudience = svcAudience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = svcKey,
                ClockSkew = TimeSpan.FromMinutes(1),
            };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminAuthorization.SuperAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AdminAuthorization.SuperAdminRole))
            .AddPolicy(AdminAuthorization.AdminCompanyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new AdminCompanyRequirement()))
            .AddPolicy(AdminAuthorization.OtAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AdminAuthorization.OtAdminRole))
            .AddPolicy(AdminAuthorization.OtModulePolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(
                    AdminAuthorization.SuperAdminRole,
                    AdminAuthorization.OtAdminRole))
            // gRPC ICT: exige el service-token (esquema IctService) + scope ict.orchestration.
            .AddPolicy(IctServicePolicy, policy => policy
                .AddAuthenticationSchemes(IctServiceScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("scope", "ict.orchestration"));

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
