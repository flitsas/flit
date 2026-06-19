using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class JwtSessionExpiryTests
{
    [Fact]
    public void ValidateToken_WhenExpired_ThrowsSecurityTokenExpiredException()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: "https://api.flit.co",
            audience: "flit-api",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: credentials);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.WriteToken(token);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://api.flit.co",
            ValidateAudience = true,
            ValidAudience = "flit-api",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.Zero,
        };

        var act = () => handler.ValidateToken(jwt, parameters, out _);

        act.Should().Throw<SecurityTokenExpiredException>();
    }
}
