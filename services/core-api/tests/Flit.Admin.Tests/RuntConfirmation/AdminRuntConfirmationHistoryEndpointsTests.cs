using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.RuntConfirmation;

/// <summary>HU #12310 — permisos (AC7) y contratos de listado/detalle/corridas del Historial. Sin consultar al proveedor.</summary>
public sealed class AdminRuntConfirmationHistoryEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/admin/runt-confirmation";
    private readonly WebApplicationFactory<Program> _factory;

    public AdminRuntConfirmationHistoryEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/attempts")]
    [InlineData("/runs")]
    [InlineData("/runs/latest")]
    public async Task AC7_SinPermiso_Returns403(string path)
    {
        using var client = Client(TestTokenFactory.CreateToken("Operador"));
        var response = await client.GetAsync(Base + path, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC7_ConSoloSettingsManage_NoVeElHistorial()
    {
        using var client = Client(Token("Operador", "runt_confirmation.settings.manage"));
        var response = await client.GetAsync(Base + "/attempts", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC1_ListadoConHistoryRead_RespondePaginado()
    {
        using var client = Client(Token("Auditor", "runt_confirmation.history.read"));
        var response = await client.GetAsync(Base + "/attempts?page=1&pageSize=10&verdict=confirmed", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("pageSize").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task AC2_DetalleInexistente_Returns404()
    {
        using var client = Client(Token("SuperAdmin"));
        (await client.GetAsync($"{Base}/attempts/{Guid.NewGuid()}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{Base}/attempts/{Guid.NewGuid()}/raw", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC3_Corridas_RespondeListaYLatest()
    {
        using var client = Client(Token("SuperAdmin"));
        var list = await client.GetAsync(Base + "/runs?page=1&pageSize=5", TestContext.Current.CancellationToken);
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        var latest = await client.GetAsync(Base + "/runs/latest", TestContext.Current.CancellationToken);
        latest.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AC6_ConsultNow_DeTramiteInexistente_Returns404()
    {
        using var client = Client(Token("SuperAdmin"));
        var response = await client.PostAsync($"{Base}/procedures/{Guid.NewGuid()}/consult-now", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Token(string role, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", "11111111-1111-1111-1111-111111111111"),
            new("role", role),
            new("role_code", role),
        };
        claims.AddRange(permissions.Select(p => new Claim("permissions", p)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))),
                SecurityAlgorithms.HmacSha256),
        });
    }
}
