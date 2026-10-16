using Flit.Api.Identity;
using Flit.Identity.Api;
using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Security.Application.Products;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using Xunit;

namespace Flit.Identity.Tests;

/// <summary>
/// Varias instancias de core-identity (réplicas, o los hosts de prueba que el CI arranca en paralelo contra la misma base)
/// sincronizan los clientes OIDC a la vez. Si una crea el cliente entre el Find y el Create de otra, OpenIddict rechaza
/// el segundo Create con <see cref="OpenIddictExceptions.ValidationException"/> («An application with the same client
/// identifier already exists»). Antes eso tumbaba el arranque del host (CI de los PR #596/#597); ahora el intento
/// siguiente encuentra el cliente y lo actualiza.
/// <para>
/// Uso de ejemplo: <c>await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => sync.StartAsync(ct)))</c> sin el cliente en la
/// base → ninguna llamada falla y el cliente queda creado una sola vez.
/// </para>
/// </summary>
public sealed class OidcClientSyncConcurrenciaTests : IClassFixture<OidcClientSyncConcurrenciaTests.Host>
{
    private const int Instancias = 8;
    private const int Rondas = 5;

    private readonly Host _host;

    public OidcClientSyncConcurrenciaTests(Host host) => _host = host;

    [Fact]
    public async Task SincronizacionesSimultaneas_DelMismoCliente_NoFallanYLoDejanCreado()
    {
        var ct = TestContext.Current.CancellationToken;
        var sync = _host.Services.GetServices<IHostedService>().OfType<OidcClientSync>().Single();
        var clientId = OidcClientSync.ProductClient(
            ProductCodes.All[0],
            _host.Services.GetRequiredService<IProductHosts>(),
            _host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OidcOptions>>().Value).ClientId!;

        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            await BorrarClienteAsync(clientId, ct);

            var sincronizar = () => Task.WhenAll(Enumerable.Range(0, Instancias).Select(_ => Task.Run(() => sync.StartAsync(ct), ct)));

            await sincronizar.Should().NotThrowAsync($"ronda {ronda + 1}: la carrera entre instancias no debe tumbar el arranque");
            (await ExisteAsync(clientId, ct)).Should().BeTrue();
        }
    }

    private async Task BorrarClienteAsync(string clientId, CancellationToken ct)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await manager.FindByClientIdAsync(clientId, ct) is { } app)
            await manager.DeleteAsync(app, ct);
    }

    private async Task<bool> ExisteAsync(string clientId, CancellationToken ct)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return await manager.FindByClientIdAsync(clientId, ct) is not null;
    }

    /// <summary>core-identity con OIDC encendido (necesario para que exista <see cref="OidcClientSync"/>).</summary>
    public sealed class Host : WebApplicationFactory<IdentityApiEntryPoint>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Suite:Oidc:Enabled", "true");
            builder.UseSetting("Suite:Hosts:Environment", "dev");
        }
    }
}
