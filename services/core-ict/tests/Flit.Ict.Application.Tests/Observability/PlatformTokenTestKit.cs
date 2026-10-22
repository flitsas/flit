using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Ict.Application.Tests.Observability;

/// <summary>Llave RSA de pruebas y fábrica de JWT de plataforma firmados (o forjados) para los tests de core-ict.</summary>
internal static class PlatformTokenTestKit
{
    public const string Issuer = "https://api.flit.co";
    public const string Audience = "flit-api";

    private static readonly RSA PlatformRsa = RSA.Create(2048);
    private static readonly RSA OtherRsa = RSA.Create(2048);

    public static string PublicKeyPem => PlatformRsa.ExportSubjectPublicKeyInfoPem();

    public static string Sign(
        string role,
        Guid? tenantId,
        string[] permissions,
        string issuer = Issuer,
        string audience = Audience,
        bool otherKey = false,
        DateTime? expires = null,
        DateTime? notBefore = null)
    {
        var claims = new List<Claim>
        {
            new("sub", "11111111-1111-1111-1111-111111111111"),
            new("role", role),
            new("role_code", role),
        };
        if (tenantId is { } tenant)
        {
            claims.Add(new Claim("tenant_id", tenant.ToString()));
        }

        claims.AddRange(permissions.Select(p => new Claim("permissions", p)));

        var credentials = new SigningCredentials(
            new RsaSecurityKey(otherKey ? OtherRsa : PlatformRsa), SecurityAlgorithms.RsaSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, claims, notBefore, expires ?? DateTime.UtcNow.AddHours(1), credentials));
    }

    /// <summary>Token SIN firma (alg:none) con los mismos claims: lo que forjaría un atacante.</summary>
    public static string Unsigned(string role, Guid? tenantId, params string[] permissions)
    {
        var claims = new List<Claim> { new("sub", "x"), new("role", role), new("role_code", role) };
        if (tenantId is { } tenant)
        {
            claims.Add(new Claim("tenant_id", tenant.ToString()));
        }

        claims.AddRange(permissions.Select(p => new Claim("permissions", p)));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            Issuer, Audience, claims, null, DateTime.UtcNow.AddHours(1)));
    }
}
