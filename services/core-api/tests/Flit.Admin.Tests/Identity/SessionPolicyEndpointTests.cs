using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #13651 (Feature #13102, Épica #12753) — <c>GET /api/v1/auth/session-policy</c>: cuánto tarda en aplicarse un
/// cambio de permisos. Modo heredado → <c>Jwt:TokenLifetimeHours</c> × 60; OIDC activo → <c>Suite:Oidc:AccessTokenMinutes</c>.
/// El valor sale de la configuración, no de un literal (AC3). La autorización corta antes de la base de datos para los
/// casos heredados, así que no requieren datos sembrados.
/// </summary>
public sealed class SessionPolicyEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/v1/auth/session-policy";

    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;

    public SessionPolicyEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Token() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "11111111-1111-1111-1111-111111111111"),
                new Claim("role", "AdminCompany"),
                new Claim("tenant_id", Guid.NewGuid().ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        return client;
    }

    private static async Task<(string Mode, int MaxMinutes)> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        // Contrato fijo: exactamente estas dos claves.
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("mode", "maxMinutes");
        return (json.GetProperty("mode").GetString()!, json.GetProperty("maxMinutes").GetInt32());
    }

    [Fact]
    public async Task SinSesion_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC1_ModoHeredado_ReportaLaVidaDelTokenEnMinutos()
    {
        var client = AuthenticatedClient(_factory.WithWebHostBuilder(b => b.UseSetting("Suite:Oidc:Enabled", "false")));

        var (mode, maxMinutes) = await ReadAsync(await client.GetAsync(Url, TestContext.Current.CancellationToken));

        mode.Should().Be("legacy");
        maxMinutes.Should().Be(12 * 60, "appsettings.json fija Jwt:TokenLifetimeHours en 12");
    }

    [Fact]
    public async Task AC3_ModoHeredado_ElMaximoSigueAJwtTokenLifetimeHours()
    {
        var client = AuthenticatedClient(_factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Suite:Oidc:Enabled", "false");
            b.UseSetting("Jwt:TokenLifetimeHours", "8");
        }));

        var (mode, maxMinutes) = await ReadAsync(await client.GetAsync(Url, TestContext.Current.CancellationToken));

        mode.Should().Be("legacy");
        maxMinutes.Should().Be(8 * 60);
    }

    [Fact]
    public async Task AC2_AC3_OidcActivo_ReportaAccessTokenMinutesConfigurado()
    {
        var oidc = OidcServerTests.WithOidc(_factory).WithWebHostBuilder(b => b.UseSetting("Suite:Oidc:AccessTokenMinutes", "30"));
        var client = AuthenticatedClient(oidc);

        var (mode, maxMinutes) = await ReadAsync(await client.GetAsync(Url, TestContext.Current.CancellationToken));

        mode.Should().Be("oidc");
        maxMinutes.Should().Be(30, "el máximo es el AccessTokenMinutes configurado, no un literal");
    }

    [Fact]
    public async Task AC2_OidcActivo_SinCambiarLaConfiguracionSonLos15MinutosDelContrato()
    {
        var client = AuthenticatedClient(OidcServerTests.WithOidc(_factory));

        var (mode, maxMinutes) = await ReadAsync(await client.GetAsync(Url, TestContext.Current.CancellationToken));

        mode.Should().Be("oidc");
        maxMinutes.Should().Be(15);
    }
}
