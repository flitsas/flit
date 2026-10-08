using System.Security.Cryptography;
using System.Text;
using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Security;
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
        var externalIssuer = configuration["ExternalJwt:Issuer"] ?? ExternalClientAuthorization.DefaultIssuer;

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
                        // HU #13087 AC5 — además del emisor configurado, rechaza siempre el pase externo.
                        ValidateIssuer = true,
                        IssuerValidator = (tokenIssuer, _, _) => ValidatePlatformIssuer(tokenIssuer, issuer, externalIssuer),
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
                // HU #13087 AC5 — incluso en este modo, un pase externo NO autentica en la plataforma:
                // sin esta comprobación entraría en todo endpoint que solo exige estar autenticado.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    IssuerValidator = (tokenIssuer, _, _) => ValidatePlatformIssuer(tokenIssuer, null, externalIssuer),
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

        // HU #13087 (Épica #12737, ADR-0067) — pase de los clientes de integración externos: esquema
        // APARTE con emisor, audiencia y llave propios (ExternalJwtKeyMaterial, compartida con el emisor en
        // este mismo proceso). Sin llave fuera de Development la llave existe pero nada la firma: cerrado.
        services.AddAuthentication().AddJwtBearer(ExternalClientAuthorization.Scheme, _ => { });
        services.AddOptions<JwtBearerOptions>(ExternalClientAuthorization.Scheme)
            .Configure<ExternalJwtKeyMaterial>((options, keyMaterial) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = keyMaterial.Issuer,
                    ValidateAudience = true,
                    ValidAudience = keyMaterial.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = keyMaterial.IsAvailable
                        ? keyMaterial.SigningKey
                        : new RsaSecurityKey(RSA.Create(2048)),
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // HU #13081 — sin pase, pase caducado o de otro emisor: 401 en problem+json con code
                // invalid_token, como el resto de errores externos (contrato v3.1 §2).
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await ExternalProblem.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                            "invalid_token", "Falta el pase o no es válido para este recurso.",
                            context.HttpContext.RequestAborted).ConfigureAwait(false);
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminAuthorization.SuperAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AdminAuthorization.SuperAdminRole))
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
                .RequireClaim("scope", "ict.orchestration"))
            // HU #13087 — endpoints externos: solo el esquema ExternalClient y el permiso del pase.
            .AddPolicy(ExternalClientAuthorization.TramitesReadPolicy, policy => policy
                .AddAuthenticationSchemes(ExternalClientAuthorization.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ExternalClientAuthorization.ScopeClaim, ExternalScopes.TramitesRead))
            .AddPolicy(ExternalClientAuthorization.TramitesPiiReadPolicy, policy => policy
                .AddAuthenticationSchemes(ExternalClientAuthorization.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ExternalClientAuthorization.ScopeClaim, ExternalScopes.TramitesPiiRead))
            // HU #13263 — envío de adjuntos: permiso propio, no basta con el de lectura.
            .AddPolicy(ExternalClientAuthorization.AttachmentsWritePolicy, policy => policy
                .AddAuthenticationSchemes(ExternalClientAuthorization.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ExternalClientAuthorization.ScopeClaim, ExternalScopes.AttachmentsWrite));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, SuperAdminForbiddenResultHandler>();

        return services;
    }

    /// <summary>
    /// Emisor aceptado por el esquema de la plataforma: nunca el del pase externo y, si hay emisor
    /// configurado, solo ese.
    /// </summary>
    internal static string ValidatePlatformIssuer(string tokenIssuer, string? platformIssuer, string externalIssuer)
    {
        if (string.Equals(tokenIssuer, externalIssuer, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(platformIssuer) && !string.Equals(tokenIssuer, platformIssuer, StringComparison.Ordinal)))
        {
            throw new SecurityTokenInvalidIssuerException($"Emisor no aceptado: {tokenIssuer}") { InvalidIssuer = tokenIssuer };
        }

        return tokenIssuer;
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
