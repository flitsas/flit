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
/// HU #13734 (F00.1-H8) — el gateway lleva <c>/api/v1/diagnostico/*</c> a core-diagnostico, igual que Comparendos: sin
/// política de JWT en la ruta (el token lo valida el servicio, <c>aud=diagnostico</c>) y el resto sigue en core-api.
/// </summary>
public sealed class DiagnosticoRoutingTests : IAsyncLifetime
{
    private WebApplication? _coreApi;
    private WebApplication? _coreDiagnostico;

    public async ValueTask InitializeAsync()
    {
        _coreApi = await StartBackendAsync("core-api");
        _coreDiagnostico = await StartBackendAsync("core-diagnostico");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var backend in new[] { _coreApi, _coreDiagnostico })
        {
            if (backend is not null)
            {
                await backend.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData("/api/v1/diagnostico/sesion")]
    [InlineData("/api/v1/diagnostico/vehiculos/ABC123")]
    public async Task RutaDeDiagnostico_LlegaACoreDiagnostico_SinTokenEnElGateway(string path)
    {
        using var gateway = Gateway();
        var response = await gateway.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("core-diagnostico", path);
    }

    [Fact]
    public void RutaDeDiagnostico_UsaSuClusterYNoExigePoliticaDelGateway()
    {
        using var gateway = Gateway();
        var route = gateway.Services.GetRequiredService<IProxyStateLookup>().GetRoutes()
            .SingleOrDefault(r => r.Config.RouteId == "diagnostico-route");

        route.Should().NotBeNull("el gateway debe declarar diagnostico-route");
        route!.Config.ClusterId.Should().Be("core-diagnostico-cluster");
        route.Config.AuthorizationPolicy.Should().BeNull();
        route.Config.Match.Path.Should().Be("/api/v1/diagnostico/{**catch-all}");
    }

    private WebApplicationFactory<Program> Gateway() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:DisableJwtPolicy", "false");
            b.UseSetting("ReverseProxy:Clusters:core-api-cluster:Destinations:core-api-1:Address", Address(_coreApi!));
            b.UseSetting(
                "ReverseProxy:Clusters:core-diagnostico-cluster:Destinations:core-diagnostico-1:Address", Address(_coreDiagnostico!));
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
