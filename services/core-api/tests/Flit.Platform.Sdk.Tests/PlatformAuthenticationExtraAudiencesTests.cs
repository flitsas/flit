using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Flit.Platform.Sdk.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Platform.Sdk.Tests;

/// <summary>
/// HU #14454 (Épica #14210, ADR-0082) — <c>Platform:Auth:ExtraAudiences</c> deja que el BFF del portal acepte, además de su propia
/// audiencia, la del producto <c>caja</c>. Es aditivo: sin la lista, solo vale la audiencia propia (lo prueba
/// <see cref="PlatformAuthenticationTests"/>).
/// </summary>
public sealed class PlatformAuthenticationExtraAudiencesTests : IAsyncLifetime
{
    private const string Issuer = "https://dev.flitsas.online/";
    private const string Audience = "personanatural";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "flit-oidc-signing-v1" };
    private readonly JwksHandler _jwks = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _jwks.Keys = [_key];
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Auth:Audience"] = Audience,
            ["Platform:Auth:ExtraAudiences:0"] = "caja",
            ["Platform:Auth:Issuers:0"] = Issuer,
            ["Platform:Auth:JwksUri"] = "http://core-identity:4025/.well-known/jwks.json",
        });
        builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
        builder.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => _jwks);
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/quien", (ClaimsPrincipal user) => Results.Ok(user.FindFirstValue("sub"))).RequireAuthorization();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("personanatural", HttpStatusCode.OK)]
    [InlineData("caja", HttpStatusCode.OK)]
    [InlineData("tramites", HttpStatusCode.Unauthorized)]
    [InlineData("plataforma", HttpStatusCode.Unauthorized)]
    public async Task SoloValenLaAudienciaPropiaYLasExtra(string audience, HttpStatusCode esperado) =>
        (await GetAsync(Token(audience))).StatusCode.Should().Be(esperado);

    [Fact]
    public async Task UnaAudienciaExtraNoRelajaElEmisorNiLaFirma()
    {
        (await GetAsync(Token("caja", issuer: "https://otro-emisor/"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync(Token("caja", key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "otra" }))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> GetAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/quien");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private string Token(string audience, string issuer = Issuer, RsaSecurityKey? key = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", "u1")]),
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key ?? _key, SecurityAlgorithms.RsaSha256),
        });
}
