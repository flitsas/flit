using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// Genera JWT de prueba. En el entorno de test no hay llave pública configurada,
/// por lo que Flit.Api acepta el token sin validar la firma (modo transitorio);
/// la firma HS256 aquí es irrelevante, solo se necesitan los claims (rol).
/// </summary>
internal static class TestTokenFactory
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    public static string CreateToken(string role)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "11111111-1111-1111-1111-111111111111"),
                new Claim("role", role),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    public static string CreateOtAdminToken(Guid tenantId, string role = "ot_admin")
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "11111111-1111-1111-1111-111111111111"),
                new Claim("role", role),
                new Claim("tenant_id", tenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>HU #11228 — token AdminCompany con tenant concreto.</summary>
    public static string CreateAdminCompanyToken(Guid tenantId, Guid? userId = null)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", (userId ?? Guid.Parse("11111111-1111-1111-1111-111111111111")).ToString()),
                new Claim("role", "AdminCompany"),
                new Claim("tenant_id", tenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>
    /// Bug #13445 — token con rol (como <c>role</c> y <c>role_code</c>, igual que <c>RsaJwtTokenIssuer</c>),
    /// tenant opcional y slugs en <c>permissions</c>: lo que evalúa <c>PermissionAuthorizationHandler</c>.
    /// </summary>
    public static string CreateTokenWithPermissions(string role, Guid? tenantId, params string[] permissions)
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

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
