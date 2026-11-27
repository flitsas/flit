using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy;

namespace Flit.Gateway.Tests;

/// <summary>
/// HU #14694 (ADR-0093) — el gateway lleva <c>/api/v1/impuestos/*</c> a core-impuestos, igual que Diagnóstico: sin
/// política de JWT en la ruta (el token lo valida el servicio, <c>aud=impuestos</c>, y el receptor de avisos
/// <c>/avisos/*</c> es anónimo con firma) y el resto sigue en core-api, incluido <c>/api/v1/webhooks/*</c>.
/// </summary>
public sealed class ImpuestosRoutingTests : IAsyncLifetime
{
    private WebApplication? _coreApi;
    private WebApplication? _coreImpuestos;

    public async ValueTask InitializeAsync()
    {
        _coreApi = await StartBackendAsync("core-api");
        _coreImpuestos = await StartBackendAsync("core-impuestos");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var backend in new[] { _coreApi, _coreImpuestos })
        {
            if (backend is not null)
            {
                await backend.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData("/api/v1/impuestos/sesion")]
    [InlineData("/api/v1/impuestos/liquidaciones/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/v1/impuestos/avisos/token-de-prueba")]
    public async Task RutaDeImpuestos_LlegaACoreImpuestos_SinTokenEnElGateway(string path)
    {
        using var gateway = Gateway();
        var response = await gateway.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("core-impuestos", path);
    }

    [Fact]
    public void RutaDeImpuestos_UsaSuClusterYNoExigePoliticaDelGateway()
    {
        using var gateway = Gateway();
        var route = gateway.Services.GetRequiredService<IProxyStateLookup>().GetRoutes()
            .SingleOrDefault(r => r.Config.RouteId == "impuestos-route");

        route.Should().NotBeNull("el gateway debe declarar impuestos-route");
        route!.Config.ClusterId.Should().Be("core-impuestos-cluster");
        route.Config.AuthorizationPolicy.Should().BeNull();
        route.Config.Match.Path.Should().Be("/api/v1/impuestos/{**catch-all}");
    }

    [Fact]
    public void WebhooksDeCoreApi_NoCambianDeCluster()
    {
        using var gateway = Gateway();
        var route = gateway.Services.GetRequiredService<IProxyStateLookup>().GetRoutes()
            .SingleOrDefault(r => r.Config.RouteId == "kyverum-webhook-public-route");

        route.Should().NotBeNull();
        route!.Config.ClusterId.Should().Be("core-api-cluster");
        route.Config.Match.Path.Should().Be("/api/v1/webhooks/{**catch-all}");
    }

    private WebApplicationFactory<Program> Gateway() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:DisableJwtPolicy", "false");
            b.UseSetting("ReverseProxy:Clusters:core-api-cluster:Destinations:core-api-1:Address", Address(_coreApi!));
            b.UseSetting(
                "ReverseProxy:Clusters:core-impuestos-cluster:Destinations:core-impuestos-1:Address", Address(_coreImpuestos!));
        });

    private static async Task<WebApplication> StartBackendAsync(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Run(context => context.Response.WriteAsync(name));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static string Address(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First() + "/";
}
