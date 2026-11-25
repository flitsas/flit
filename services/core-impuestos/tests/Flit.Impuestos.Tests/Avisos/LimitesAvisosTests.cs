using System.Net;
using Flit.Impuestos.Api.Modulos.Avisos;
using Flit.Impuestos.Api.Plataforma;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Impuestos.Tests.Avisos;

/// <summary>
/// HU #14694 (IMP-3 / M-1) — borde del webhook <c>POST /api/v1/impuestos/avisos/{token}</c>: detrás del proxy, el límite por IP
/// usa la IP real de <c>X-Forwarded-For</c> (solo si la conexión viene de un proxy configurado), un token sin la forma (43
/// caracteres base64url) no consume cupo y hay un límite global aparte del de cada IP. Host mínimo con el mismo registro y el
/// mismo orden de middleware que <c>Program</c> (proxies → límites); el endpoint responde 410 a un token sin forma igual que el
/// real y 200 al resto (el procesamiento del aviso lo cubre <see cref="AvisosEndpointsTests"/>).
/// </summary>
/// <example>
/// <code>
/// await using var host = await HostAsync(porIp: 2, global: 100);
/// var status = await EnviarAsync(host, TokenValido, remota: "10.114.40.7", reenviadaPor: "200.1.1.1");
/// </code>
/// </example>
public sealed class LimitesAvisosTests
{
    private const string Prefijo = "/api/v1/impuestos";
    private const string Proxy = "10.114.40.7";
    private static readonly string TokenValido = new('A', 43);

    [Fact]
    public async Task Dos_IP_reales_distintas_detras_del_proxy_tienen_cupos_separados()
    {
        await using var host = await HostAsync(porIp: 2, global: 100);

        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.1")).Should().Be(200);
        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.1")).Should().Be(200);
        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.1")).Should().Be(429, "la primera IP agotó su cupo");
        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.2")).Should().Be(200, "otra IP real detrás del mismo proxy tiene su propio cupo");
    }

    [Fact]
    public async Task Una_conexion_que_no_viene_de_un_proxy_configurado_no_elige_su_IP_con_la_cabecera()
    {
        await using var host = await HostAsync(porIp: 2, global: 100);

        (await EnviarAsync(host, TokenValido, "8.8.8.8", "200.1.1.1")).Should().Be(200);
        (await EnviarAsync(host, TokenValido, "8.8.8.8", "200.1.1.2")).Should().Be(200);
        (await EnviarAsync(host, TokenValido, "8.8.8.8", "200.1.1.3")).Should().Be(429, "la cabecera de un cliente directo no cuenta");
    }

    [Fact]
    public async Task Un_token_sin_la_forma_responde_410_y_no_consume_cupo()
    {
        await using var host = await HostAsync(porIp: 2, global: 2);

        for (var i = 0; i < 5; i++)
            (await EnviarAsync(host, "corto", Proxy, "200.1.1.1")).Should().Be(410);
        (await EnviarAsync(host, new string('A', 42) + "*", Proxy, "200.1.1.1")).Should().Be(410, "43 caracteres pero no base64url");

        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.1")).Should().Be(200, "los 410 de forma no gastaron el cupo de la IP ni el global");
        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.1")).Should().Be(200);
    }

    [Fact]
    public async Task El_limite_global_corta_aunque_cada_IP_tenga_cupo()
    {
        await using var host = await HostAsync(porIp: 3, global: 3);

        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.1")).Should().Be(200);
        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.2")).Should().Be(200);
        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.3")).Should().Be(200);
        (await EnviarAsync(host, TokenValido, Proxy, "200.1.1.4")).Should().Be(429, "el global es aparte del de cada IP");
    }

    [Fact]
    public void Un_limite_global_menor_que_el_de_cada_IP_no_arranca()
    {
        var config = Config(porIp: 60, global: 10);
        var act = () => new ServiceCollection().AddLimitesSolicitudes(config);
        act.Should().Throw<InvalidOperationException>().WithMessage("*AvisosGlobalesPorMinuto*");
    }

    private static IConfiguration Config(int porIp, int global) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{LimitesSolicitudes.Seccion}:AvisosPorIpPorMinuto"] = porIp.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [$"{LimitesSolicitudes.Seccion}:AvisosGlobalesPorMinuto"] = global.ToString(System.Globalization.CultureInfo.InvariantCulture),
    }).Build();

    private static async Task<WebApplication> HostAsync(int porIp, int global)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddConfiguration(Config(porIp, global));
        builder.Services.AddProxiesConfiables(builder.Configuration);
        builder.Services.AddLimitesSolicitudes(builder.Configuration);
        var app = builder.Build();
        app.UseProxiesConfiables();
        app.UseRateLimiter();
        app.MapPost(Prefijo + AvisosEndpoints.Segmento + "/{token}", (string token) =>
                AvisosEndpoints.TokenConForma(token) ? Results.Ok() : Results.StatusCode(StatusCodes.Status410Gone))
            .RequireRateLimiting(LimitesSolicitudes.Avisos);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<int> EnviarAsync(WebApplication host, string token, string remota, string reenviadaPor)
    {
        var contexto = await host.GetTestServer().SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Post;
            c.Request.Path = Prefijo + AvisosEndpoints.Segmento + "/" + token;
            c.Connection.RemoteIpAddress = IPAddress.Parse(remota);
            c.Request.Headers["X-Forwarded-For"] = reenviadaPor;
        }, TestContext.Current.CancellationToken);
        return contexto.Response.StatusCode;
    }
}
