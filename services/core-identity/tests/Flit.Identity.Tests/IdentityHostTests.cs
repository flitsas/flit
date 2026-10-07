using System.Net;
using Flit.Api.Identity;
using Flit.Identity.Api;
using Flit.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Identity.Tests;

/// <summary>
/// HU #13233 (Epic #13217) — core-identity arranca solo con lo del login: contenedor validado al construir, únicamente
/// los procesos de OIDC, únicamente las rutas de la frontera §4, listo con el esquema migrado y el mismo nombre de
/// Data Protection que core-api. Usa la base de <c>ConnectionStrings__Core</c> (la misma que las pruebas de core-api).
/// </summary>
public sealed class IdentityHostTests : IClassFixture<IdentityHostTests.Host>
{
    /// <summary>Lo único que atiende core-identity (frontera §4), más la salud.</summary>
    private static readonly string[] IdentityPrefixes =
    [
        "/connect/", "/.well-known/", "/api/v1/auth/", "/api/v1/platform/", "/api/v1/public/branding", "/health",
        "/api/v1/health",
        "/flit.identidad.v1.IdentidadService/", // HU #13334, solo con Identidad:GrpcPort
    ];

    private readonly WebApplicationFactory<IdentityApiEntryPoint> _identity;

    public IdentityHostTests(Host host) => _identity = host;

    [Fact]
    public void ArrancaConElContenedorValidado_YSoloLosProcesosDeOidc()
    {
        var flitHostedServices = _identity.Services.GetServices<IHostedService>()
            .Select(s => s.GetType())
            .Where(t => t.Assembly.GetName().Name?.StartsWith("Flit.", StringComparison.Ordinal) == true)
            .Select(t => t.Name);

        // HU #13359: core-identity publica sus correos por su outbox siempre (OutboxPublisherService del SDK).
        flitHostedServices.Should().BeEquivalentTo(["OidcClientSync", "OidcPruningService", "OutboxPublisherService`1"]);
    }

    [Fact]
    public void SoloExponeLasRutasDelLogin()
    {
        var routes = _identity.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Distinct()
            .ToList();

        routes.Should().NotBeEmpty();
        routes.Should().OnlyContain(r => IdentityPrefixes.Any(p => r.StartsWith(p, StringComparison.Ordinal)), "frontera §4");
        routes.Should().Contain(["/connect/authorize", "/connect/token", "/connect/login", "/api/v1/auth/login", "/api/v1/platform/me/apps"]);
    }

    [Fact]
    public async Task ConElEsquemaMigrado_EstaListo_YNoAtiendeNegocio()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _identity.CreateClient();

        (await client.GetAsync("/health/ready", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/public/banners/active", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void UsaElMismoNombreDeDataProtectionQueCoreApi()
    {
        // Si difiriera, core-identity no podría leer la cookie de la sesión del hub ni las llaves de firma que cifró
        // core-api (y viceversa): todos tendrían que volver a iniciar sesión.
        _identity.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator
            .Should().Be(IdentityInfrastructureExtensions.DataProtectionApplicationName)
            .And.Be("flit-core-api");
    }

    [Fact]
    public void LeeLaConfiguracionBaseDeCoreApi()
    {
        // appsettings.json de core-api, enlazado: sin él no habría emisor, dominios reservados ni clientes OIDC.
        var configuration = _identity.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        configuration["Jwt:Issuer"].Should().NotBeNullOrEmpty();
        configuration["Suite:Hosts:Root"].Should().Be("flitsas.online");
        configuration["Suite:Oidc:SigningKeyId"].Should().Be("flit-oidc-signing-v1");
    }

    [Fact]
    public void ElServidorOidcEstaEncendido()
    {
        _identity.Services.GetRequiredService<IOptions<OidcOptions>>().Value.Enabled.Should().BeTrue();
    }

    /// <summary>core-identity con OIDC encendido sobre la base de las pruebas.</summary>
    public sealed class Host : WebApplicationFactory<IdentityApiEntryPoint>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Suite:Oidc:Enabled", "true");
            builder.UseSetting("Suite:Hosts:Environment", "dev");
        }
    }
}
