using System.Net;
using System.Net.Http.Headers;
using Flit.Ict.Api.Authorization;
using Flit.Ict.Api.Endpoints;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Trazabilidad;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Observability;

/// <summary>
/// Bug de seguridad (core-ict aceptaba cualquier JWT solo decodificado): el token de plataforma se valida con firma,
/// emisor, audiencia y vencimiento antes de conceder acceso a Logs, Trazabilidad, revelado de PII y clientes.
/// </summary>
public sealed class IctPlatformTokenValidationTests : IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
    private const string LogsUrl = "/api/v1/ict/logs";
    private const string BandejaUrl = "/api/v1/ict/trazabilidad/tramites";
    private const string RevelarUrl = "/api/v1/ict/trazabilidad/tramites/1001/datos/revelar";

    private readonly IRevelarDatosPersonalesQuery _revelar = Substitute.For<IRevelarDatosPersonalesQuery>();
    private WebApplication? _app;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<IIntegrationLogQuery>());
        builder.Services.AddSingleton(Substitute.For<IIctAlertMetricsQuery>());
        builder.Services.AddSingleton(Substitute.For<ITrazabilidadBandejaQuery>());
        builder.Services.AddSingleton(Substitute.For<ITiposTramiteQuery>());
        builder.Services.AddSingleton(Substitute.For<IRecorridoTramiteQuery>());
        builder.Services.AddSingleton(Substitute.For<IConsultasFuenteQuery>());
        builder.Services.AddSingleton(Substitute.For<IDatosTramiteQuery>());
        builder.Services.AddSingleton(Substitute.For<ILogTramiteQuery>());
        builder.Services.AddSingleton(_revelar);
        builder.Configuration["Jwt:PublicKeyPem"] = PlatformTokenTestKit.PublicKeyPem;
        builder.Services.AddIctPlatformTokenValidation(builder.Configuration, builder.Environment);

        _app = builder.Build();
        _app.UseIctPlatformToken();
        _app.MapIctObservabilityEndpoints();
        _app.MapIctTrazabilidadEndpoints();
        await _app.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task TokenValido_SuperAdmin_AbreLogs()
    {
        var response = await Get(LogsUrl, PlatformTokenTestKit.Sign("SuperAdmin", null, []));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TokenValido_ConPermisoYTenant_AbreTrazabilidad()
    {
        var response = await Get(BandejaUrl, PlatformTokenTestKit.Sign("admin_tramites", TenantId, ["ict.trazabilidad.read"]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SinToken_NoAbreNada()
    {
        var response = await Get(LogsUrl, null);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    public static TheoryData<string, string> TokensInvalidos => new()
    {
        { "alg:none", PlatformTokenTestKit.Unsigned("SuperAdmin", null) },
        { "otra llave", PlatformTokenTestKit.Sign("SuperAdmin", null, [], otherKey: true) },
        { "emisor incorrecto", PlatformTokenTestKit.Sign("SuperAdmin", null, [], issuer: "https://evil.example") },
        { "audiencia incorrecta", PlatformTokenTestKit.Sign("SuperAdmin", null, [], audience: "otra-api") },
        {
            "vencido",
            PlatformTokenTestKit.Sign("SuperAdmin", null, [], expires: DateTime.UtcNow.AddMinutes(-10), notBefore: DateTime.UtcNow.AddHours(-1))
        },
    };

    [Theory]
    [MemberData(nameof(TokensInvalidos))]
    public async Task TokenInvalido_SuperAdminForjado_NoAbreLogsNiTrazabilidad(string caso, string token)
    {
        _ = caso;
        (await Get(LogsUrl, token)).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await Get(BandejaUrl, token)).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await Read(token)).Should().BeEquivalentTo(new PlatformAccess(false, false, null));
    }

    [Fact]
    public async Task PermisoPiiRevealForjado_NoRevelaDatosPersonales()
    {
        var forged = PlatformTokenTestKit.Sign("admin_tramites", TenantId, ["ict.trazabilidad.read", "ict.pii.reveal"], otherKey: true);
        var unsigned = PlatformTokenTestKit.Unsigned("admin_tramites", TenantId, "ict.trazabilidad.read", "ict.pii.reveal");

        foreach (var token in new[] { forged, unsigned })
        {
            var response = await Client(token).PostAsync(RevelarUrl, content: null, Ct);

            response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        }

        await _revelar.DidNotReceiveWithAnyArgs().RevelarAsync(default, default, default!, Ct);
    }

    [Theory]
    [InlineData("NOTSUPERADMIN")]
    [InlineData("SUPERVISOR")]
    [InlineData("SuperAdminX")]
    public async Task RolQueSoloContieneSuper_NoEsSuperAdmin(string role)
    {
        var token = PlatformTokenTestKit.Sign(role, null, []);
        var access = await Read(token);

        access.IsSuperAdmin.Should().BeFalse();
        access.HasPiiRevealAccess.Should().BeFalse();
        access.HasIctTrazabilidadAccess.Should().BeFalse();
        (await Get(LogsUrl, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Configuración: fail-fast ──────────────────────────────────────────────────────────────

    [Fact]
    public void SinLlave_FueraDeDevelopment_FallaAlArrancar()
    {
        var act = () => Register(new ConfigurationBuilder().Build(), Environments.Production);

        act.Should().Throw<InvalidOperationException>().WithMessage("*llave pública*");
    }

    [Fact]
    public void ApagarValidacion_FueraDeDevelopment_FallaAlArrancar()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:ValidateIssuedTokens"] = "false" })
            .Build();

        var act = () => Register(config, Environments.Production);

        act.Should().Throw<InvalidOperationException>().WithMessage("*solo se permite en Development*");
    }

    [Fact]
    public void Development_SinLlave_ArrancaSinValidar()
    {
        var act = () => Register(new ConfigurationBuilder().Build(), Environments.Development);

        act.Should().NotThrow();
    }

    [Fact]
    public void Development_ConValidacionExplicitaSinLlave_FallaAlArrancar()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:ValidateIssuedTokens"] = "true" })
            .Build();

        var act = () => Register(config, Environments.Development);

        act.Should().Throw<InvalidOperationException>().WithMessage("*llave pública*");
    }

    [Fact]
    public void ConJwksUri_FueraDeDevelopment_Arranca()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:JwksUri"] = "http://gateway:4002/.well-known/jwks.json" })
            .Build();

        var act = () => Register(config, Environments.Production);

        act.Should().NotThrow();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static void Register(IConfiguration config, string environmentName) =>
        new ServiceCollection().AddIctPlatformTokenValidation(
            config,
            new TestEnvironment { EnvironmentName = environmentName });

    private HttpClient Client(string? token)
    {
        var client = _app!.GetTestClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private Task<HttpResponseMessage> Get(string url, string? token) => Client(token).GetAsync(url, Ct);

    private async Task<PlatformAccess> Read(string token)
    {
        using var scope = _app!.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";
        await PlatformTokenAuthentication.AuthenticateAsync(context);
        return PlatformAccessReader.Read(context);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
