using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Flit.Ict.Domain.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Ict.Infrastructure.Security;

/// <summary>
/// Emisor del JWT de ICT (RS256), espejo del RsaJwtTokenIssuer de core-api pero con audiencia/issuer
/// y llave propios, y claims mínimos (sin roles/permissions de plataforma).
/// </summary>
public sealed class IctRsaJwtTokenIssuer(IctJwtKeyMaterial keyMaterial, IOptions<IctJwtSettings> options)
    : IIctJwtTokenIssuer
{
    private readonly IctJwtSettings _settings = options.Value;

    public IssuedIctToken Issue(Guid integrationClientId, Guid tenantId, string tenantName, IReadOnlyList<string> scopes)
    {
        var credentials = new SigningCredentials(keyMaterial.SigningKey, SecurityAlgorithms.RsaSha256);
        var expires = DateTime.UtcNow.AddHours(_settings.TokenLifetimeHours);
        var expiresInSeconds = _settings.TokenLifetimeHours * 3600;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, integrationClientId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("tenant_name", tenantName),
        };

        // Un claim "scope" por cada scope (para RequireClaim) + array explícito "scopes".
        foreach (var scope in scopes)
        {
            claims.Add(new Claim("scope", scope));
        }

        claims.Add(new Claim("scopes", JsonSerializer.Serialize(scopes), JsonClaimValueTypes.JsonArray));

        var token = new JwtSecurityToken(
            issuer: keyMaterial.Issuer,
            audience: keyMaterial.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        var handler = new JwtSecurityTokenHandler();
        return new IssuedIctToken(handler.WriteToken(token), expiresInSeconds);
    }
}
