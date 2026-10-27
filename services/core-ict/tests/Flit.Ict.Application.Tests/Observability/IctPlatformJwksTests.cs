using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Ict.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Ict.Application.Tests.Observability;

/// <summary>
/// Cadena real de llaves en los VPS: core-api publica en <c>/api/v1/internal/jwks.json</c> la llave pública (kid
/// <c>flit-api-v1</c>) con que firma el token de usuario y core-ict la lee por <c>Jwt:JwksUri</c>. Aquí un servidor
/// Kestrel local hace de core-api.
/// </summary>
public sealed class IctPlatformJwksTests : IAsyncLifetime
{
    private const string KeyId = "flit-api-v1";
    private readonly RSA _apiKey = RSA.Create(2048);
    private readonly RSA _otherKey = RSA.Create(2048);
    private WebApplication? _coreApi;
    private WebApplication? _ict;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var p = _apiKey.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "RSA", use = "sig", alg = "RS256", kid = KeyId, n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) },
            },
        });

        var api = WebApplication.CreateSlimBuilder();
        api.WebHost.UseUrls("http://127.0.0.1:0");
        _coreApi = api.Build();
        _coreApi.MapGet("/api/v1/internal/jwks.json", () => Results.Text(jwks, "application/json"));
        await _coreApi.StartAsync(Ct);
        var address = _coreApi.Urls.First();

        var ict = WebApplication.CreateSlimBuilder();
        ict.Configuration["Jwt:JwksUri"] = $"{address}/api/v1/internal/jwks.json";
        ict.Services.AddIctPlatformTokenValidation(ict.Configuration, ict.Environment); // Production: valida siempre
        _ict = ict.Build();
        await _ict.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ict is not null) await _ict.DisposeAsync();
        if (_coreApi is not null) await _coreApi.DisposeAsync();
        _apiKey.Dispose();
        _otherKey.Dispose();
    }

    [Fact]
    public async Task TokenFirmadoConFlitApiV1_ServidaPorElJwksDeCoreApi_SeAcepta()
    {
        var access = await Read(Sign(_apiKey));

        access.IsSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task TokenFirmadoConOtraLlave_SeRechaza()
    {
        var access = await Read(Sign(_otherKey));

        access.Should().BeEquivalentTo(new PlatformAccess(false, false, null));
    }

    private static string Sign(RSA key) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = PlatformTokenTestKit.Issuer,
        Audience = PlatformTokenTestKit.Audience,
        Subject = new ClaimsIdentity([new Claim("sub", "u"), new Claim("role", "SuperAdmin")]),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
    });

    private async Task<PlatformAccess> Read(string token)
    {
        using var scope = _ict!.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";
        await PlatformTokenAuthentication.AuthenticateAsync(context);
        return PlatformAccessReader.Read(context);
    }
}
