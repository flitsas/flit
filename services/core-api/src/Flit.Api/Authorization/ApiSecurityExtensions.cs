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
/// transitorio coherente con el Gateway mientras el login no es obligatorio — salvo que
/// <c>Jwt:ValidateIssuedTokens</c> esté encendida (HU #12896): entonces valida con la llave de firma propia.
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

        // Epic #13217 (HU #13232): el JWT de la plataforma (de siempre y del hub) se valida igual en core-identity.
        services.AddFlitTokenValidation(configuration, environment);

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
            .AddPolicy(AdminAuthorization.SuperAdminPolicy, policy => policy.RequireSuperAdmin())
            .AddPolicy(AdminAuthorization.AdminCompanyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new AdminCompanyRequirement()))
            .AddPolicy(AdminAuthorization.GroupHeadCompanyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new GroupHeadCompanyRequirement()))
            .AddPolicy(AdminAuthorization.MarcaBlancaHeadCompanyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new MarcaBlancaHeadCompanyRequirement()))
            .AddPolicy(AdminAuthorization.OtAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AdminAuthorization.OtAdminRole))
            .AddPolicy(AdminAuthorization.UserAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(
                    AdminAuthorization.SuperAdminRole,
                    AdminAuthorization.AdminCompanyRole,
                    AdminAuthorization.OtAdminRole))
            .AddPolicy(AdminAuthorization.OtModulePolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new OtModuleRequirement()))
            // HU #12859 (Feature #12848, Épica #12751) — SuperAdmin u ot_admin exclusivamente,
            // sin el bypass de entity_type=TRANSIT_OFFICE de OtModulePolicy (solo Prelación).
            .AddPolicy(AdminAuthorization.OtAdminOrSuperAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new OtAdminOrSuperAdminRequirement()))
            // gRPC ICT: exige el service-token (esquema IctService) + scope ict.orchestration.
            .AddPolicy(IctServicePolicy, policy => policy
                .AddAuthenticationSchemes(IctServiceScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("scope", "ict.orchestration"));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, SuperAdminForbiddenResultHandler>();

        return services;
    }
}
