using System.Security.Cryptography;
using System.Text.Json;
using Flit.Api.Endpoints.Internal;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>El JWKS interno publica SOLO la parte pública de la llave que firma el token de usuario (core-ict valida con él).</summary>
public sealed class InternalJwksTests
{
    private const string KeyId = "flit-api-v1";

    [Fact]
    public void Jwks_PublicaSoloParametrosPublicos_ConKid()
    {
        using var rsa = RSA.Create(2048);
        var json = JsonSerializer.Serialize(InternalJwksEndpoints.BuildJwks(Material(rsa), KeyId));

        using var doc = JsonDocument.Parse(json);
        var key = doc.RootElement.GetProperty("keys").EnumerateArray().Single();
        key.GetProperty("kid").GetString().Should().Be(KeyId);
        key.GetProperty("kty").GetString().Should().Be("RSA");
        foreach (var privado in new[] { "d", "p", "q", "dp", "dq", "qi" })
        {
            key.TryGetProperty(privado, out _).Should().BeFalse($"el parámetro privado '{privado}' no se publica");
        }
    }

    [Fact]
    public void TokenFirmadoPorLaLlave_ValidaContraElJwks_YOtraLlaveNo()
    {
        using var rsa = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var jwks = new JsonWebKeySet(JsonSerializer.Serialize(InternalJwksEndpoints.BuildJwks(Material(rsa), KeyId)));
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            IssuerSigningKeys = jwks.GetSigningKeys(),
        };

        var good = Sign(rsa);
        var bad = Sign(other);

        new JsonWebTokenHandler().ValidateTokenAsync(good, parameters).Result.IsValid.Should().BeTrue();
        new JsonWebTokenHandler().ValidateTokenAsync(bad, parameters).Result.IsValid.Should().BeFalse();
    }

    private static JwtKeyMaterial Material(RSA rsa) => new() { SigningKey = new RsaSecurityKey(rsa) };

    private static string Sign(RSA rsa) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", "u")]),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
    });
}
