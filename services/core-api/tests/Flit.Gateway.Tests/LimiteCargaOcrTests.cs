using System.Net;
using Flit.Gateway.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Gateway.Tests;

/// <summary>
/// GDC punto 10 — el gateway corre sobre Kestrel real (el TestServer no aplica límites de cuerpo): la carga OCR de
/// Comparendos llega al servicio por encima de los 30.000.000 bytes por defecto de Kestrel y hasta 100 MB de archivos;
/// las demás rutas conservan el límite por defecto.
/// </summary>
public sealed class LimiteCargaOcrTests : IAsyncLifetime
{
    private const long Mb = 1024 * 1024;
    private WebApplication? _comparendos;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = null);
        _comparendos = builder.Build();
        _comparendos.Run(async context =>
        {
            long total = 0;
            var buffer = new byte[81920];
            int leidos;
            while ((leidos = await context.Request.Body.ReadAsync(buffer)) > 0)
                total += leidos;
            await context.Response.WriteAsync(total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
        await _comparendos.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_comparendos is not null)
            await _comparendos.DisposeAsync();
    }

    [Fact]
    public async Task CargaOcr_De40Mb_LlegaAComparendos()
    {
        using var gateway = Gateway();
        var response = await Post(gateway, "/api/v1/comparendos/ocr/paquetes", 40 * Mb);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be((40 * Mb).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CargaOcr_De100MbMasElSobre_LlegaAComparendos()
    {
        using var gateway = Gateway();
        var response = await Post(gateway, "/api/v1/comparendos/ocr/paquetes", LimiteCargaMiddleware.LimiteOcrBytes);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CargaOcr_PorEncimaDelLimite_Da413()
    {
        using var gateway = Gateway();
        var response = await Post(gateway, "/api/v1/comparendos/ocr/paquetes", LimiteCargaMiddleware.LimiteOcrBytes + 1);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task OtraRuta_ConservaElLimitePorDefectoDeKestrel()
    {
        using var gateway = Gateway();
        var response = await Post(gateway, "/api/v1/comparendos/comparendos/importar", 40 * Mb);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    private static async Task<HttpResponseMessage> Post(WebApplicationFactory<Program> gateway, string path, long bytes)
    {
        using var client = gateway.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);
        var contenido = new ByteArrayContent(new byte[bytes]);
        contenido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        try
        {
            return await client.PostAsync(path, contenido, TestContext.Current.CancellationToken);
        }
        catch (HttpRequestException)
        {
            // Kestrel puede cerrar la conexión mientras el cliente aún escribe; solo vale como corte si no hubo respuesta.
            return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge);
        }
    }

    private WebApplicationFactory<Program> Gateway()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:DisableJwtPolicy", "false");
            b.UseSetting("ReverseProxy:Clusters:core-comparendos-cluster:Destinations:core-comparendos-1:Address", Address(_comparendos!));
        });
        factory.UseKestrel(0);
        factory.StartServer();
        return factory;
    }

    private static string Address(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First() + "/";
}
