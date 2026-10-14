using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Tenancy;
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
/// HU #13336 (Epic #13316) — un servicio sin la base de Identidad valida tokens contra el JWKS de Identidad: firma,
/// emisor y su propia audiencia. El JWKS se sirve desde un manejador falso que cuenta las lecturas.
/// </summary>
public sealed class PlatformAuthenticationTests : IAsyncLifetime
{
    private const string Issuer = "https://dev.flitsas.online/";
    private const string Audience = "consultas";

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
            ["Platform:Auth:Issuers:0"] = Issuer,
            ["Platform:Auth:JwksUri"] = "http://core-identity:4025/.well-known/jwks.json",
        });
        builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
        builder.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => _jwks);
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/quien", (ClaimsPrincipal user, IPlatformTenantAccessor tenant) =>
            Results.Ok(new { sub = user.FindFirstValue("sub"), tenant = tenant.Current.TenantId, service = tenant.Current.IsService }))
            .RequireAuthorization();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task TokenDeServicioValido_Entra_YActuaPorLaEmpresaDeLaCabecera()
    {
        var tenant = Guid.NewGuid();
        var response = await GetAsync(Token(), tenant);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        body.GetProperty("sub").GetString().Should().Be("svc-tramites");
        body.GetProperty("tenant").GetGuid().Should().Be(tenant);
        body.GetProperty("service").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("tramites", Issuer)] // token para otro servicio
    [InlineData(Audience, "https://otro-emisor/")]
    public async Task OtraAudienciaUOtroEmisor_401(string audience, string issuer) =>
        (await GetAsync(Token(audience: audience, issuer: issuer))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task FirmadoConOtraLlave_401() =>
        (await GetAsync(Token(key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "otra" }))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Vencido_401() =>
        (await GetAsync(Token(expires: DateTime.UtcNow.AddMinutes(-5)))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task ElJwksSeLeeUnaVez_YSeReleeCuandoRotaLaLlave()
    {
        (await GetAsync(Token())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync(Token())).StatusCode.Should().Be(HttpStatusCode.OK);
        _jwks.Reads.Should().Be(1);

        // Identidad rota la llave: el primer token con la llave nueva fuerza una relectura.
        var rotated = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "flit-oidc-signing-v2" };
        _jwks.Keys = [_key, rotated];
        await Task.Delay(TimeSpan.FromSeconds(31), TestContext.Current.CancellationToken); // RefreshInterval mínimo entre relecturas
        (await GetAsync(Token(key: rotated))).StatusCode.Should().Be(HttpStatusCode.OK);
        _jwks.Reads.Should().Be(2);
    }

    [Fact]
    public void SinConfiguracionCompleta_NoArranca()
    {
        var act = () => new ServiceCollection().AddFlitPlatformAuthentication(new ConfigurationBuilder().Build());
        act.Should().Throw<InvalidOperationException>().WithMessage("*Platform:Auth*");
    }

    private Task<HttpResponseMessage> GetAsync(string token, Guid? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/quien");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tenant is { } t)
            request.Headers.Add(PlatformTenantContext.Header, t.ToString());
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private string Token(string audience = Audience, string issuer = Issuer, RsaSecurityKey? key = null, DateTime? expires = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", "svc-tramites"), new Claim("scope", "platform.consultas")]),
            NotBefore = (expires ?? DateTime.UtcNow.AddMinutes(10)).AddMinutes(-15),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key ?? _key, SecurityAlgorithms.RsaSha256),
        });
}
