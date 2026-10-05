using System.Net;
using Flit.Gateway.Configuration;
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
/// HU #13225 (Epic #13217) — con <c>Gateway:IdentityCluster:Enabled</c> las rutas del login van a core-identity y el
/// resto sigue en core-api; apagada, todo va a core-api como antes. Se prueba de punta a punta con dos destinos falsos
/// que responden su nombre, así cuentan también la precedencia de rutas de ASP.NET y la política de cada ruta.
/// </summary>
public sealed class IdentityClusterRoutingTests : IAsyncLifetime
{
    private static readonly string[] IdentityPaths =
    [
        "/connect/authorize", "/.well-known/openid-configuration", "/api/v1/auth/login", "/api/v1/platform/me/apps",
        "/api/v1/platform/issuers", "/api/v1/public/branding", "/api/v1/public/branding/logos/abc",
    ];

    private static readonly string[] ApiPaths =
    [
        "/api/v1/tramites/instances", "/api/v1/security/users", "/api/v1/superadmin/roles", "/api/v1/public/banners/active",
        "/api/v1/me/branding",
    ];

    private WebApplication? _coreApi;
    private WebApplication? _coreIdentity;

    public async ValueTask InitializeAsync()
    {
        _coreApi = await StartBackendAsync("core-api");
        _coreIdentity = await StartBackendAsync("core-identity");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var backend in new[] { _coreApi, _coreIdentity })
        {
            if (backend is not null)
            {
                await backend.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task BanderaApagada_TodoVaACoreApi()
    {
        using var gateway = Gateway(identityClusterEnabled: false);
        var client = gateway.CreateClient();

        foreach (var path in IdentityPaths.Concat(ApiPaths))
        {
            (await BackendFor(client, path)).Should().Be("core-api", path);
        }
    }

    [Fact]
    public async Task BanderaEncendida_LasRutasDelLoginVanACoreIdentity_YElRestoACoreApi()
    {
        using var gateway = Gateway(identityClusterEnabled: true);
        var client = gateway.CreateClient();

        foreach (var path in IdentityPaths)
        {
            (await BackendFor(client, path)).Should().Be("core-identity", path);
        }

        foreach (var path in ApiPaths)
        {
            (await BackendFor(client, path)).Should().Be("core-api", path);
        }
    }

    [Fact]
    public async Task BanderaEncendida_SiCoreIdentityCae_ElLoginSigueEnCoreApi()
    {
        using var gateway = Gateway(identityClusterEnabled: true);
        var client = gateway.CreateClient();
        (await BackendFor(client, "/api/v1/auth/login")).Should().Be("core-identity");

        await _coreIdentity!.DisposeAsync();
        _coreIdentity = null;

        // El chequeo activo (/health/ready cada segundo en la prueba) saca a core-identity tras dos fallos seguidos.
        string backend = "";
        for (var i = 0; i < 20 && backend != "core-api"; i++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            var response = await client.GetAsync("/api/v1/auth/login", TestContext.Current.CancellationToken);
            backend = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken) : "";
        }

        backend.Should().Be("core-api", "core-api atiende las mismas rutas del login y queda de respaldo");
        (await BackendFor(client, "/connect/authorize")).Should().Be("core-api");
    }

    [Fact]
    public void BanderaEncendida_SoloCambiaElDestino_NoLaPolitica()
    {
        using var off = Gateway(identityClusterEnabled: false);
        using var on = Gateway(identityClusterEnabled: true);

        var before = Routes(off);
        var after = Routes(on);

        after.Keys.Should().BeEquivalentTo(before.Keys);
        foreach (var (id, route) in after)
        {
            route.Policy.Should().Be(before[id].Policy, id);
            route.Cluster.Should().Be(
                before[id].Identity ? IdentityClusterProxyConfigFilter.ClusterId : before[id].Cluster, id);
        }

        before.Where(r => r.Value.Identity).Select(r => r.Key).Should().BeEquivalentTo(
            "oidc-connect-route", "oidc-well-known-route", "auth-public-route", "platform-route", "public-branding-route");
    }

    private WebApplicationFactory<Program> Gateway(bool identityClusterEnabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:DisableJwtPolicy", "false");
            b.UseSetting(IdentityClusterProxyConfigFilter.EnabledKey, identityClusterEnabled ? "true" : "false");
            b.UseSetting("ReverseProxy:Clusters:core-api-cluster:Destinations:core-api-1:Address", Address(_coreApi!));
            b.UseSetting("ReverseProxy:Clusters:core-identity-cluster:Destinations:a-core-identity:Address", Address(_coreIdentity!));
            b.UseSetting("ReverseProxy:Clusters:core-identity-cluster:Destinations:b-core-api:Address", Address(_coreApi!));
            b.UseSetting("ReverseProxy:Clusters:core-identity-cluster:HealthCheck:Active:Interval", "00:00:01");
        });

    private static async Task<string> BackendFor(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static Dictionary<string, (string Cluster, string? Policy, bool Identity)> Routes(WebApplicationFactory<Program> gateway) =>
        gateway.Services.GetRequiredService<IProxyStateLookup>().GetRoutes().ToDictionary(
            r => r.Config.RouteId,
            r => (r.Config.ClusterId!, r.Config.AuthorizationPolicy, IdentityClusterProxyConfigFilter.IsIdentityRoute(r.Config)));

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
