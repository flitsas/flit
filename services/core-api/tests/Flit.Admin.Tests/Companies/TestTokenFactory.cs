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
}
