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
        Guid roleId,
        string roleCode,
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
            new("role_id", roleId.ToString()),
            new("role_code", roleCode),
            // "role" es el RoleClaimType que exige la policy SuperAdmin del módulo Admin
            // (HU #10189). Se emite además de "role_code" para que los tokens de login
            // sean válidos para RequireRole sin acoplar ambos módulos.
            new("role", roleCode),
        };

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
