using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.Identity.Rounds;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Plataforma;

/// <summary>
/// <c>/api/v1/admin/platform/identity-rounds</c>: configurador GLOBAL de rondas de identidad. Solo el Super Admin FLIT (permiso
/// <c>identity_rounds.manage</c>, solo SuperAdmin). El almacén y la auditoría se sustituyen por dobles en memoria.
/// </summary>
public sealed class AdminIdentityRoundsEndpointsTests : IClassFixture<AdminIdentityRoundsEndpointsTests.RoundsFactory>
{
    private const string BaseUrl = "/api/v1/admin/platform/identity-rounds";
    private static readonly Guid SuperAdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly RoundsFactory _factory;

    public AdminIdentityRoundsEndpointsTests(RoundsFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Get_SuperAdmin_DevuelveLaConfiguracionPorDefecto()
    {
        using var client = ClientWith(TokenWith("SuperAdmin"));

        var response = await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("rondasMaximas").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("disparadorAlerta").GetString().Should().Be("al_maximo");
        doc.RootElement.GetProperty("correosAlerta").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Put_SuperAdmin_GuardaNormalizaYAudita()
    {
        using var client = ClientWith(TokenWith("SuperAdmin"));

        var response = await client.PutAsJsonAsync(
            BaseUrl, new { rondasMaximas = 5, disparadorAlerta = "cada_ronda", correosAlerta = new[] { " Ops@Flit.CO " } }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("rondasMaximas").GetInt32().Should().Be(5);
        doc.RootElement.GetProperty("correosAlerta")[0].GetString().Should().Be("ops@flit.co");
        _factory.Store.Config.RondasMaximas.Should().Be(5);
        _factory.Auditor.Actors.Should().Equal(SuperAdminUserId);
    }

    [Theory]
    [InlineData(0, "al_maximo", "rondasMaximas")]
    [InlineData(11, "al_maximo", "rondasMaximas")]
    [InlineData(3, "nunca", "disparadorAlerta")]
    public async Task Put_ValoresInvalidos_Returns400ConElCampo_YNadaCambia(int rondas, string disparador, string campo)
    {
        using var client = ClientWith(TokenWith("SuperAdmin"));

        var response = await client.PutAsJsonAsync(
            BaseUrl, new { rondasMaximas = rondas, disparadorAlerta = disparador, correosAlerta = Array.Empty<string>() }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("errors").TryGetProperty(campo, out _).Should().BeTrue();
        _factory.Store.Saves.Should().Be(0);
        _factory.Auditor.Actors.Should().BeEmpty();
    }

    [Fact]
    public async Task Put_CorreosInvalidosORepetidos_Returns400()
    {
        using var client = ClientWith(TokenWith("SuperAdmin"));

        var response = await client.PutAsJsonAsync(
            BaseUrl, new { rondasMaximas = 3, disparadorAlerta = "al_maximo", correosAlerta = new[] { "malo", "a@flit.co", "A@FLIT.CO" } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("errors").TryGetProperty("correosAlerta", out _).Should().BeTrue();
        _factory.Store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Put_SinRondasMaximas_Returns400()
    {
        using var client = ClientWith(TokenWith("SuperAdmin"));

        var response = await client.PutAsJsonAsync(BaseUrl, new { disparadorAlerta = "al_maximo" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task SinSesion_401_YConOtrosRoles_403_AunSiendoAdminCompany()
    {
        using var anonimo = _factory.CreateClient();
        (await anonimo.GetAsync(BaseUrl, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PutAsJsonAsync(BaseUrl, new { rondasMaximas = 4, disparadorAlerta = "al_maximo" }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        foreach (var rol in new[] { "AdminCompany", "Operador" })
        {
            using var client = ClientWith(TokenWith(rol));
            (await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden, rol);
            (await client.PutAsJsonAsync(BaseUrl, new { rondasMaximas = 4, disparadorAlerta = "al_maximo" }, TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, rol);
        }

        _factory.Store.Saves.Should().Be(0);
    }

    [Fact]
    public void ElPermisoEsSoloSuperAdmin()
    {
        Flit.Modules.Security.Application.Roles.PrivilegeCeiling.IsSuperAdminOnly("identity_rounds.manage").Should().BeTrue();
        Flit.Modules.Security.Application.Roles.PrivilegeCeiling.IsPlatformOnly("identity_rounds.manage").Should().BeTrue();
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string TokenWith(string role)
    {
        var claims = new List<Claim>
        {
            new("sub", SuperAdminUserId.ToString()),
            new("role", role),
            new("role_code", role),
        };

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

    public sealed class FakeStore : IIdentityRoundsStore
    {
        public IdentityRoundsConfig Config { get; private set; } = IdentityRoundsConfig.Default;

        public int Saves { get; private set; }

        public void Reset()
        {
            Config = IdentityRoundsConfig.Default;
            Saves = 0;
        }

        public Task<IdentityRoundsConfig> GetConfigAsync(CancellationToken ct = default) => Task.FromResult(Config);

        public Task SaveConfigAsync(IdentityRoundsConfig config, Guid? updatedBy, DateTimeOffset now, CancellationToken ct = default)
        {
            Config = config;
            Saves++;
            return Task.CompletedTask;
        }

        public Task<bool> TryRegisterAlertAsync(IdentityRoundsAlertRecord alert, CancellationToken ct = default) => Task.FromResult(true);

        public Task CompleteAlertAsync(Guid alertId, string resultado, CancellationToken ct = default) => Task.CompletedTask;
    }

    public sealed class FakeAuditor : IIdentityRoundsConfigAuditor
    {
        public List<Guid?> Actors { get; } = [];

        public Task WriteAsync(Guid? actorUserId, IdentityRoundsConfig before, IdentityRoundsConfig after, CancellationToken ct = default)
        {
            Actors.Add(actorUserId);
            return Task.CompletedTask;
        }
    }

    public sealed class RoundsFactory : WebApplicationFactory<Program>
    {
        public FakeStore Store { get; } = new();

        public FakeAuditor Auditor { get; } = new();

        public void Reset()
        {
            Store.Reset();
            Auditor.Actors.Clear();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IIdentityRoundsStore>(_ => Store);
                services.AddScoped<IIdentityRoundsConfigAuditor>(_ => Auditor);
            });
        }
    }
}
