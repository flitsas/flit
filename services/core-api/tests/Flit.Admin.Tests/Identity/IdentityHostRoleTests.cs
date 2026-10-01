using System.Net;
using Flit.Api.Hosting;
using Flit.Api.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #13224 (Epic #13217) — el mismo programa con el papel <c>identity</c> (core-identity): solo los procesos de
/// OIDC, solo las rutas del login (iguales a las de core-api) y <c>/health/ready</c>. Ver
/// <c>docs/suite/identidad-frontera.md</c> §3, §4 y §8.
/// </summary>
public sealed class IdentityHostRoleTests : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>Lo único que atiende core-identity (frontera §4), más la salud.</summary>
    private static readonly string[] IdentityPrefixes =
    [
        "/connect/", "/.well-known/", "/api/v1/auth/", "/api/v1/platform/", "/api/v1/public/branding",
        "/health", "/api/v1/health",
    ];

    private readonly WebApplicationFactory<Program> _api;
    private readonly WebApplicationFactory<Program> _identity;

    public IdentityHostRoleTests(WebApplicationFactory<Program> factory)
    {
        _api = OidcServerTests.WithOidc(factory);
        _identity = _api.WithWebHostBuilder(b => b.UseSetting(HostRoles.ConfigKey, "identity"));
    }

    [Fact]
    public void PapelIdentity_SoloCorreLosProcesosDeOidc()
    {
        FlitHostedServices(_identity).Should().BeEquivalentTo([typeof(OidcClientSync), typeof(OidcPruningService)]);
    }

    [Fact]
    public void PapelApi_ConservaLosProcesosDeNegocio()
    {
        var api = FlitHostedServices(_api);

        api.Should().Contain(IdentityHosting.IdentityHostedServices);
        api.Should().HaveCountGreaterThan(IdentityHosting.IdentityHostedServices.Count, "core-api sigue con colas, RUNT, Quipux…");
    }

    [Fact]
    public void PapelIdentity_SoloExponeLasRutasDelLogin_IgualesALasDeCoreApi()
    {
        var identity = Routes(_identity);
        var api = Routes(_api);

        identity.Should().NotBeEmpty();
        identity.Should().OnlyContain(r => IdentityPrefixes.Any(p => r.Pattern.StartsWith(p, StringComparison.Ordinal)), "frontera §4");
        api.Should().Contain(identity, "con la bandera del gateway apagada, core-api atiende las mismas rutas");
        identity.Should().Contain(r => r.Pattern == "/connect/authorize");
        identity.Should().Contain(r => r.Pattern == "/api/v1/auth/login");
        identity.Should().Contain(r => r.Pattern.StartsWith("/api/v1/platform/me/apps", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PapelIdentity_NoAtiendeRutasDeNegocio()
    {
        var ct = TestContext.Current.CancellationToken;

        // Ruta pública de negocio (sin token): core-api la atiende, core-identity no la conoce.
        const string BusinessRoute = "/api/v1/public/banners/active";

        (await _api.CreateClient().GetAsync(BusinessRoute, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _identity.CreateClient().GetAsync(BusinessRoute, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Readiness_ConLaBaseMigrada_EstaListo()
    {
        var ct = TestContext.Current.CancellationToken;

        foreach (var factory in new[] { _identity, _api })
        {
            var response = await factory.CreateClient().GetAsync("/health/ready", ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private static Type[] FlitHostedServices(WebApplicationFactory<Program> factory) =>
        factory.Services.GetServices<IHostedService>()
            .Select(s => s.GetType())
            .Where(t => t.Assembly.GetName().Name?.StartsWith("Flit.", StringComparison.Ordinal) == true)
            .ToArray();

    private static (string Pattern, string Methods)[] Routes(WebApplicationFactory<Program> factory) =>
        factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => (
                Pattern: "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                Methods: string.Join(",", e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Order() ?? Enumerable.Empty<string>())))
            .Distinct()
            .ToArray();
}
