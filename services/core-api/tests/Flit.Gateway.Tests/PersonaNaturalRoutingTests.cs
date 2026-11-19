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
/// HU #14038 (Feature #14022, Épica #14021) — el gateway reenvía <c>/api/v1/persona-natural/*</c> al BFF del portal y no
/// cambia a dónde va nada más. Dos destinos falsos que responden su nombre, para ver también la precedencia de rutas.
/// </summary>
public sealed class PersonaNaturalRoutingTests : IAsyncLifetime
{
    private WebApplication? _coreApi;
    private WebApplication? _portal;

    public async ValueTask InitializeAsync()
    {
        _coreApi = await StartBackendAsync("core-api");
        _portal = await StartBackendAsync("core-personanatural");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var backend in new[] { _coreApi, _portal })
        {
            if (backend is not null)
            {
                await backend.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData("/api/v1/persona-natural/sesiones/reanudar")]
    [InlineData("/api/v1/persona-natural/solicitudes/3f2a")]
    public async Task LaRutaPublicaDelPortal_VaAlBff(string path)
    {
        using var gateway = Gateway();

        var respuesta = await gateway.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("core-personanatural");
    }

    [Theory]
    [InlineData("/api/v1/tramites/instances")]
    [InlineData("/api/v1/public/banners/active")]
    [InlineData("/api/v1/persona-naturalx/algo")]
    public async Task LoDemas_SigueIgual_VaACoreApi(string path)
    {
        using var gateway = Gateway();

        var respuesta = await gateway.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("core-api");
    }

    [Fact]
    public void LaRutaDelPortal_NoPideJwtDeFlit_PorqueElCiudadanoNoTieneCuenta()
    {
        using var gateway = Gateway();

        var ruta = gateway.Services.GetRequiredService<IProxyStateLookup>().GetRoutes()
            .Single(r => r.Config.RouteId == "persona-natural-route").Config;

        ruta.ClusterId.Should().Be("core-personanatural-cluster");
        ruta.AuthorizationPolicy.Should().BeNull("el BFF valida su propio captcha y su sesión por trámite");
    }

    private WebApplicationFactory<Program> Gateway() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:DisableJwtPolicy", "false");
            b.UseSetting("ReverseProxy:Clusters:core-api-cluster:Destinations:core-api-1:Address", Address(_coreApi!));
            b.UseSetting("ReverseProxy:Clusters:core-personanatural-cluster:Destinations:core-personanatural-1:Address", Address(_portal!));
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
