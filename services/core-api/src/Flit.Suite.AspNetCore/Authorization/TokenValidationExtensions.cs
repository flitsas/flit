using System.Security.Cryptography;
using System.Text.Json;
using Flit.Api.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Api.Authorization;

/// <summary>
/// Epic #13217 (HU #13232) — cómo se valida el JWT de la plataforma, igual en core-api y en core-identity: el de
/// siempre (llave pública configurada o la propia persistente con <c>Jwt:ValidateIssuedTokens</c>) y, con la suite, los
/// del hub OIDC; un token inválido responde <c>SESSION_EXPIRED</c> y uno de una sesión cerrada se rechaza.
/// </summary>
public static class TokenValidationExtensions
{
    /// <summary>Esquema JwtBearer por defecto de la plataforma. Salió de <c>ApiSecurityExtensions.AddApiSecurity</c>.</summary>
    public static IServiceCollection AddFlitTokenValidation(
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

        // HU #12896 (A-03): sin llave pública configurada, la API puede validar sus PROPIOS tokens con la parte
        // pública de la llave con que firma (JwtKeyMaterial, persistente con Jwt:PersistSigningKey). Cierra el
        // modo permisivo de arriba sin poner llaves en el .env. Fail-closed: si la llave no se puede cargar, las
        // peticiones autenticadas fallan en vez de aceptarse.
        if (signingKey is null && jwtSection.GetValue<bool>("ValidateIssuedTokens"))
        {
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .PostConfigure<Flit.Infrastructure.Security.JwtKeyMaterial>((options, keyMaterial) =>
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = keyMaterial.Issuer,
                        ValidateAudience = true,
                        ValidAudience = keyMaterial.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new RsaSecurityKey(keyMaterial.SigningKey.Rsa.ExportParameters(includePrivateParameters: false)),
                        RoleClaimType = AdminAuthorization.RoleClaimType,
                        ClockSkew = TimeSpan.FromSeconds(30),
                    });
        }

        return services;
    }

    /// <summary>
    /// 401 con <c>SESSION_EXPIRED</c> ante un token presente pero inválido, y rechazo de tokens de sesiones cerradas
    /// (<see cref="OidcSessionCheck"/>). Salió de <c>Program.cs</c> de core-api.
    /// </summary>
    public static IServiceCollection AddFlitSessionExpiredResponses(this IServiceCollection services)
    {
        // Respuesta 401 con código SESSION_EXPIRED para tokens expirados (HU #10168, AC3).
        // Aditivo sobre AddApiSecurity: solo fija Events, sin alterar TokenValidationParameters.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.Events = new JwtBearerEvents
            {
                // Cierre de sesión en toda la suite: un token OIDC de una sesión ya cerrada se rechaza (SESSION_EXPIRED).
                OnTokenValidated = Flit.Api.Identity.OidcSessionCheck.ValidateAsync,
                // HU #12896: la respuesta SESSION_EXPIRED la escribe SOLO OnChallenge. Antes también la escribía
                // OnAuthenticationFailed y el segundo intento reventaba («the response has already started»); nunca
                // se había visto porque sin validar el vencimiento ningún token llegaba aquí como vencido.
                OnChallenge = context =>
                {
                    // HU #12896: cualquier token PRESENTE pero inválido (vencido, mal firmado, de otro emisor o audiencia)
                    // responde SESSION_EXPIRED, que el frontend ya maneja (borra el token y lleva al login). Sin esto, el
                    // día que la API empieza a validar la firma, las sesiones abiertas con la llave efímera anterior
                    // quedarían en un 401 sin código y el usuario no volvería al login.
                    if (context.AuthenticateFailure is SecurityTokenException)
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        return context.Response.WriteAsync(JsonSerializer.Serialize(new
                        {
                            code = "SESSION_EXPIRED",
                            message = "Session expired. Please sign in again.",
                        }));
                    }

                    return Task.CompletedTask;
                },
            });
        return services;
    }

    /// <summary>La política SuperAdmin, la misma en los dos procesos.</summary>
    public static AuthorizationPolicyBuilder RequireSuperAdmin(this AuthorizationPolicyBuilder policy) =>
        policy.RequireAuthenticatedUser().RequireRole(AdminAuthorization.SuperAdminRole);

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
