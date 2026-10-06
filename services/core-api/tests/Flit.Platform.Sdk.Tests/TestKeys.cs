using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Platform.Sdk.Tests;

/// <summary>Llave de firma de prueba, su JWKS servido por un manejador falso y tokens firmados con ella.</summary>
internal sealed class TestKeys
{
    public const string Issuer = "https://hub.prueba/";

    public RsaSecurityKey Key { get; } = new(RSA.Create(2048)) { KeyId = "flit-oidc-signing-v1" };

    public string Token(string sub, string audience, string? scope = null, RsaSecurityKey? key = null, DateTime? expires = null, string issuer = Issuer)
    {
        var claims = new List<Claim> { new("sub", sub) };
        if (scope is not null)
            claims.Add(new Claim("scope", scope));
        var until = expires ?? DateTime.UtcNow.AddMinutes(10);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = until.AddMinutes(-15),
            Expires = until,
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256),
        });
    }
}

/// <summary>Sirve un JWKS con las llaves públicas de <see cref="Keys"/> y cuenta las lecturas.</summary>
internal sealed class JwksHandler : HttpMessageHandler
{
    private int _reads;

    public IReadOnlyList<RsaSecurityKey> Keys { get; set; } = [];

    public int Reads => _reads;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        var set = new JsonWebKeySet();
        foreach (var key in Keys)
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(key.Rsa.ExportParameters(false)) { KeyId = key.KeyId });
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;
            set.Keys.Add(jwk);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { keys = set.Keys })) });
    }
}
