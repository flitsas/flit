using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Application.Plataforma.DrFlit;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace Flit.Admin.Tests.Plataforma;

/// <summary>
/// HU #14771 — borde HTTP de GET/PUT <c>/api/v1/admin/plataforma/dr-flit</c> y GET <c>/api/v1/dr-flit/ui</c>.
/// </summary>
public sealed class AdminPlataformaDrFlitEndpointTests : IClassFixture<AdminPlataformaDrFlitEndpointTests.Factory>
{
    private const string AdminRuta = "/api/v1/admin/plataforma/dr-flit";
    private const string UiRuta = "/api/v1/dr-flit/ui";
    private static readonly Guid UsuarioId = Guid.Parse("14771000-0000-4000-8000-0000000000b1");
    private static readonly Guid TenantId = Guid.Parse("14771000-0000-4000-8000-0000000000c1");

    private readonly Factory _factory;

    public AdminPlataformaDrFlitEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AC1_Get_Admin_SuperAdmin_200_ConUiEnabledYRowVersion()
    {
        var response = await Cliente(Token("SuperAdmin")).GetAsync(AdminRuta, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var raiz = await Raiz(response);
        raiz.GetProperty("uiEnabled").GetBoolean().Should().BeFalse();
        raiz.GetProperty("rowVersion").GetInt64().Should().Be(3);
    }

    [Fact]
    public async Task AC2_Put_Admin_SuperAdmin_200_ActualizaUiEnabled()
    {
        var body = new StringContent("""{"uiEnabled":true,"rowVersion":3}""", Encoding.UTF8, "application/json");
        var response = await Cliente(Token("SuperAdmin")).PutAsync(AdminRuta, body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var raiz = await Raiz(response);
        raiz.GetProperty("uiEnabled").GetBoolean().Should().BeTrue();
        raiz.GetProperty("rowVersion").GetInt64().Should().Be(4);
        await _factory.Service.Received(1).UpdateAsync(
            Arg.Is<UpdateDrFlitPlatformSettingsRequest>(r => r.UiEnabled && r.RowVersion == 3),
            UsuarioId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_Put_Admin_RowVersionConflict_409()
    {
        _factory.Service.UpdateAsync(Arg.Any<UpdateDrFlitPlatformSettingsRequest>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((DrFlitPlatformSettingsWriteStatus.Conflict, (DrFlitPlatformSettingsView?)null));

        var body = new StringContent("""{"uiEnabled":true,"rowVersion":1}""", Encoding.UTF8, "application/json");
        var response = await Cliente(Token("SuperAdmin")).PutAsync(AdminRuta, body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("row_version_conflict");
    }

    [Fact]
    public async Task AC3_Put_Admin_SinSuperAdmin_403()
    {
        var body = new StringContent("""{"uiEnabled":true}""", Encoding.UTF8, "application/json");
        var response = await Cliente(Token("AdminCompany")).PutAsync(AdminRuta, body, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC1_Get_Ui_Autenticado_200_UiEnabled()
    {
        _factory.Service.GetUiEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);

        // SuperAdmin no exige tenant en el middleware de compañía; cubre el contrato autenticado.
        var response = await Cliente(Token("SuperAdmin")).GetAsync(UiRuta, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await Raiz(response)).GetProperty("uiEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AC3_Get_Ui_SinToken_401()
    {
        var response = await Cliente(null).GetAsync(UiRuta, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient Cliente(string? token)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Raiz(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(json).RootElement;
    }

    private static string Token(string role, bool conTenant = false)
    {
        var claims = new List<Claim>
        {
            new("sub", UsuarioId.ToString()),
            new("role", role),
            new("role_code", role),
        };
        if (conTenant)
            claims.Add(new Claim("tenant_id", TenantId.ToString()));
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

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public IDrFlitPlatformSettingsAdminService Service { get; } = Substitute.For<IDrFlitPlatformSettingsAdminService>();

        public Factory() => Reiniciar();

        public void Reiniciar()
        {
            Service.ClearSubstitute();
            Service.GetAsync(Arg.Any<CancellationToken>())
                .Returns(new DrFlitPlatformSettingsView(false, 3, null, null));
            Service.GetUiEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);
            Service.UpdateAsync(Arg.Any<UpdateDrFlitPlatformSettingsRequest>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var req = call.Arg<UpdateDrFlitPlatformSettingsRequest>();
                    var userId = call.Arg<Guid?>();
                    return (DrFlitPlatformSettingsWriteStatus.Ok,
                        new DrFlitPlatformSettingsView(req.UiEnabled, 4, DateTimeOffset.UtcNow, userId));
                });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services => services.AddScoped(_ => Service));
        }
    }
}
