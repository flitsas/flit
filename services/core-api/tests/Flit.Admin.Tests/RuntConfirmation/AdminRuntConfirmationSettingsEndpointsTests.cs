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

/// <summary>
/// HU #12277 (Feature #12276) — autorización por permiso (AC4), contrato de lectura (AC1) y 400 con
/// el campo identificado (AC2) de <c>/api/v1/admin/runt-confirmation/settings</c>. Corre contra la
/// base de desarrollo que levanta <see cref="WebApplicationFactory{TEntryPoint}"/>; ninguna prueba
/// deja la configuración distinta de como la encontró.
/// </summary>
public sealed class AdminRuntConfirmationSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SettingsUrl = "/api/v1/admin/runt-confirmation/settings";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminRuntConfirmationSettingsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(SettingsUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC4_Get_SinPermisoYNoSuperAdmin_Returns403()
    {
        using var client = Client(TestTokenFactory.CreateToken("Operador"));
        var response = await client.GetAsync(SettingsUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC4_Put_ConSoloHistoryRead_Returns403()
    {
        using var client = Client(TokenWithPermissions("Auditor", "runt_confirmation.history.read"));
        var response = await client.PutAsJsonAsync(SettingsUrl, ValidBody(), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC1_Get_ComoSuperAdmin_DevuelveLaConfiguracionUnica()
    {
        using var client = Client(SuperAdminToken());
        var response = await client.GetAsync(SettingsUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = doc.RootElement;
        root.GetProperty("providerKey").GetString().Should().BeOneOf("kyverum_runt", "verifik");
        root.GetProperty("runAtLocal").GetString().Should().MatchRegex("^\\d{2}:\\d{2}$");
        root.GetProperty("graceDays").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        root.GetProperty("discrepancyAfterRuns").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        root.GetProperty("maxAttempts").GetInt32().Should().BeGreaterThanOrEqualTo(root.GetProperty("discrepancyAfterRuns").GetInt32());
    }

    [Fact]
    public async Task Get_ConElPermisoSinSerSuperAdmin_Returns200()
    {
        using var client = Client(TokenWithPermissions("Operador", "runt_confirmation.settings.manage"));
        var response = await client.GetAsync(SettingsUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AC2_Put_ConGraceDaysNegativo_Returns400ConElCampo()
    {
        using var client = Client(SuperAdminToken());
        var response = await client.PutAsJsonAsync(
            SettingsUrl,
            new { enabled = false, runAtLocal = "02:00", providerKey = "kyverum_runt", graceDays = -1, discrepancyAfterRuns = 3, maxAttempts = 10 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("error").GetString().Should().Be("configuracion_invalida");
        doc.RootElement.GetProperty("errors").EnumerateArray().Single().GetProperty("field").GetString().Should().Be("graceDays");
    }

    [Fact]
    public async Task AC2_Put_ConProveedorDesconocido_Returns400ConElCampo()
    {
        using var client = Client(SuperAdminToken());
        var response = await client.PutAsJsonAsync(
            SettingsUrl,
            new { enabled = false, runAtLocal = "02:00", providerKey = "runt_directo", graceDays = 0, discrepancyAfterRuns = 3, maxAttempts = 10 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("errors").EnumerateArray().Single().GetProperty("field").GetString().Should().Be("providerKey");
    }

    [Fact]
    public async Task Put_ConLosMismosValores_Returns200_YNoCambiaNada()
    {
        using var client = Client(SuperAdminToken());
        var before = await client.GetFromJsonAsync<JsonElement>(SettingsUrl, TestContext.Current.CancellationToken);

        var same = new
        {
            enabled = before.GetProperty("enabled").GetBoolean(),
            runAtLocal = before.GetProperty("runAtLocal").GetString(),
            providerKey = before.GetProperty("providerKey").GetString(),
            graceDays = before.GetProperty("graceDays").GetInt32(),
            discrepancyAfterRuns = before.GetProperty("discrepancyAfterRuns").GetInt32(),
            maxAttempts = before.GetProperty("maxAttempts").GetInt32(),
        };

        var response = await client.PutAsJsonAsync(SettingsUrl, same, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<JsonElement>(SettingsUrl, TestContext.Current.CancellationToken);
        after.GetProperty("updatedAt").ToString().Should().Be(before.GetProperty("updatedAt").ToString(), "un guardado sin cambios no toca la fila");
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object ValidBody() =>
        new { enabled = false, runAtLocal = "02:00", providerKey = "kyverum_runt", graceDays = 0, discrepancyAfterRuns = 3, maxAttempts = 10 };

    /// <summary>
    /// SuperAdmin con <c>role</c> Y <c>role_code</c>, como los emite <c>RsaJwtTokenIssuer</c>: el bypass de
    /// <c>PermissionAuthorizationHandler</c> lee <c>role_code</c>, que <c>TestTokenFactory.CreateToken</c> no incluye.
    /// </summary>
    private static string SuperAdminToken() => TokenWithPermissions("SuperAdmin");

    /// <summary>Token con claim <c>permissions</c>, que <c>TestTokenFactory</c> no emite (misma firma dummy).</summary>
    private static string TokenWithPermissions(string role, params string[] permissions)
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
