using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Infrastructure.Security;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "https://api.flit.co";

    public string Audience { get; init; } = "flit-api";

    public string PrivateKeyPem { get; init; } = string.Empty;

    public string PrivateKeyPath { get; init; } = string.Empty;

    public int TokenLifetimeHours { get; init; } = 12;
}

public sealed class RsaJwtTokenIssuer(JwtKeyMaterial keyMaterial, IOptions<JwtSettings> options) : IJwtTokenIssuer
{
    private readonly JwtSettings _settings = options.Value;

    public IssuedAccessToken IssueToken(
        Guid userId,
        string email,
        Guid tenantId,
        string tenantName,
        string companyNit,
        string entityType,
        IReadOnlyList<UserRoleSnapshot> roles,
        IReadOnlyList<string> permissionSlugs)
    {
        var credentials = new SigningCredentials(keyMaterial.SigningKey, SecurityAlgorithms.RsaSha256);
        var expires = DateTime.UtcNow.AddHours(_settings.TokenLifetimeHours);
        var expiresInSeconds = (int)(_settings.TokenLifetimeHours * 3600);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new("tenant_id", tenantId.ToString()),
            new("tenant_name", tenantName),
            // HU #10616 (AC1/AC2/AC4) — nombre/NIT de la empresa u organismo de tránsito asociado
            // al tenant y tipo de entidad ("COMPANY" | "TRANSIT_OFFICE"). "company_name" reutiliza
            // el mismo valor que "tenant_name" (en este dominio el tenant ES la empresa/OT).
            // "company_nit" se emite igual (incluso vacío, AC4) sin romper la emisión del token.
            new("company_name", tenantName),
            new("company_nit", companyNit),
            new("entity_type", entityType),
        };

        // HU #10506 — multi-rol: un claim POR CADA rol activo (no uno solo), para que
        // RequireRole/IsInRole (que evalúan contra CUALQUIER claim que matchee el tipo
        // configurado) sigan funcionando sin cambios en las policies existentes.
        foreach (var role in roles)
        {
            claims.Add(new Claim("role_id", role.Id.ToString()));
            claims.Add(new Claim("role_code", role.Code));
            // "role" es el RoleClaimType que exige la policy SuperAdmin del módulo Admin
            // (HU #10189). Se emite además de "role_code" para que los tokens de login
            // sean válidos para RequireRole sin acoplar ambos módulos.
            claims.Add(new Claim("role", role.Code));
        }

        // Claim de conveniencia con el array explícito de roles (mismo cuidado que
        // "permissions": un solo elemento no debe colapsar a string plano — ver más abajo).
        var rolesJson = JsonSerializer.Serialize(roles.Select(r => new { id = r.Id, code = r.Code }));
        claims.Add(new Claim("roles", rolesJson, JsonClaimValueTypes.JsonArray));

        // Un solo Claim("permissions", ...) por slug colapsa a un string plano (no array) en el
        // JSON del token cuando permissionSlugs tiene exactamente un elemento — el serializador de
        // JwtSecurityToken solo emite un array JSON si hay 2+ claims del mismo tipo. Con roles OT
        // curados por SuperAdmin (con frecuencia 1 solo permiso) esto rompe el parseo del frontend
        // (que espera siempre un array). Se serializa explícitamente como JSON array, incluso vacío.
        claims.Add(new Claim(
            "permissions",
            JsonSerializer.Serialize(permissionSlugs),
            JsonClaimValueTypes.JsonArray));

        var token = new JwtSecurityToken(
            issuer: keyMaterial.Issuer,
            audience: keyMaterial.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        var handler = new JwtSecurityTokenHandler();
        return new IssuedAccessToken
        {
            Token = handler.WriteToken(token),
            ExpiresInSeconds = expiresInSeconds,
        };
    }
}
