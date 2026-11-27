using System.Net;
using Flit.Consultas.Api.Impuesto;
using Flit.Consultas.Api.Impuesto.Avisos;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto.Avisos;

/// <summary>
/// HU #14711 — <c>POST /api/v1/impuesto/avisos/{token}</c> sobre un host de prueba: anónimo, sin cuerpo en ninguna respuesta, aviso
/// válido 200, token desconocido 401, cuerpo de más de 256 KB 413, y los límites de core-impuestos: 60 por minuto por IP (el 61 es
/// 429 con <c>Retry-After</c>) sin que un token sin forma consuma cupo.
/// <para>Uso de ejemplo: <c>await cliente.PostAsync("/api/v1/impuesto/avisos/" + token, cuerpo)</c>.</para>
/// </summary>
public sealed class AvisosImpuestoEndpointsTests : IAsyncLifetime
{
    private static readonly Guid Empresa = Guid.Parse("0194aaaa-0000-7000-8000-000000000001");
    private static readonly Guid Trabajo = Guid.Parse("0194cccc-0000-7000-8000-000000000003");
    private readonly SeguimientoEnMemoria _seguimiento = new();
    private readonly FuenteSecretoFalsa _secretos = new();
    private const string CabeceraIp = "X-Prueba-Ip";
    private WebApplication? _app;
    private HttpClient? _cliente;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _secretos.PorEmpresa[Empresa] = new SecretoWebhookImpuesto(true, AvisosDePrueba.Secreto, null, null, 1);
        _seguimiento.Sembrar(new SeguimientoImpuesto(Trabajo, "ext-1", DateTimeOffset.UtcNow, "queued"));
        var registro = new RegistroEnMemoria(_seguimiento);
        registro.Empresas[Trabajo] = Empresa;

        // Development a propósito: si una falla se escapara, la DeveloperExceptionPage respondería 500 con cuerpo (L2).
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(OpcionesAvisoImpuesto.Crear("https://consultas.example", AvisosDePrueba.Llave));
        builder.Services.AddSingleton<IFuenteSecretoWebhookImpuesto>(_secretos);
        builder.Services.AddSingleton<IRegistroAvisosImpuesto>(registro);
        builder.Services.AddSingleton<BuzonAvisosImpuesto>();
        builder.Services.AddSingleton<ProcesadorAvisoImpuesto>();
        builder.Services.AddLimitesAvisosImpuesto();
        _app = builder.Build();
        // M-2: la IP de la conexión la fija la prueba (X-Prueba-Ip) para simular varios orígenes.
        _app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.TryGetValue(CabeceraIp, out var ip) && IPAddress.TryParse(ip.ToString(), out var dir))
                ctx.Connection.RemoteIpAddress = dir;
            await next(ctx);
        });
        _app.UseRateLimiter();
        _app.MapAvisosImpuesto();
        await _app.StartAsync(Ct);
        _cliente = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _cliente?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    private Task<HttpResponseMessage> Post(string token, byte[] cuerpo, bool firmar = true, string? ip = null)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, AvisosImpuestoEndpoints.Prefijo + "/" + token) { Content = new ByteArrayContent(cuerpo) };
        if (ip is not null)
            pedido.Headers.Add(CabeceraIp, ip);
        pedido.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var ts = AvisosDePrueba.Ts(DateTimeOffset.UtcNow);
        pedido.Headers.Add("X-Flit-Timestamp", ts);
        pedido.Headers.Add("X-Flit-Event", "consulta.finalizada");
        pedido.Headers.Add("X-Flit-Delivery", Guid.NewGuid().ToString());
        if (firmar)
            pedido.Headers.Add("X-Flit-Signature", AvisosDePrueba.Firmar(AvisosDePrueba.Secreto, ts, cuerpo));
        return _cliente!.SendAsync(pedido, Ct);
    }

    [Fact]
    public async Task AvisoValido_200_SinCuerpo_YQuedaEnElBuzon()
    {
        var respuesta = await Post(TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave), AvisosDePrueba.Cuerpo("ext-1", 1));

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await respuesta.Content.ReadAsByteArrayAsync(Ct)).Should().BeEmpty();
        var buzon = _app!.Services.GetRequiredService<BuzonAvisosImpuesto>();
        (await buzon.EsperarAsync(Empresa, "ext-1", TimeSpan.Zero, Ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task TokenDesconocidoOFirmaInvalida_401_SinDetalle()
    {
        var desconocido = await Post(TokenAvisoImpuesto.Emitir(Guid.NewGuid(), System.Text.Encoding.UTF8.GetBytes("otra-llave-de-prueba-0123456789-abcdefgh")),
            AvisosDePrueba.Cuerpo("ext-1", 1));
        var sinFirma = await Post(TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave), AvisosDePrueba.Cuerpo("ext-1", 1), firmar: false);

        desconocido.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sinFirma.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await desconocido.Content.ReadAsByteArrayAsync(Ct)).Should().BeEmpty();
        (await sinFirma.Content.ReadAsByteArrayAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task CuerpoDeMasDe256KB_413()
    {
        var grande = new byte[AvisosImpuestoEndpoints.MaxCuerpo + 1];
        Array.Fill(grande, (byte)' ');

        var respuesta = await Post(TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave), grande);

        respuesta.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Limites_60PorMinutoPorIp_El61Es429ConRetryAfter_YElTokenSinFormaNoConsume()
    {
        for (var i = 0; i < 70; i++)
            (await Post("sin-forma", [])).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "un token sin forma no consume cupo");

        var token = TokenAvisoImpuesto.Emitir(Guid.NewGuid(), AvisosDePrueba.Llave);
        for (var i = 0; i < LimitesAvisosImpuesto.PorIpPorMinuto; i++)
            (await Post(token, AvisosDePrueba.Cuerpo("ext-x", 1))).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, $"petición {i + 1}");

        var rechazada = await Post(token, AvisosDePrueba.Cuerpo("ext-x", 1));

        rechazada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rechazada.Headers.RetryAfter.Should().NotBeNull();
        LimitesAvisosImpuesto.GlobalesPorMinuto.Should().Be(600);
        LimitesAvisosImpuesto.PorIpPorMinuto.Should().Be(60);
    }

    private static string Ip(int n) => "10.0.0." + n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task M2_ElGlobal_600DeTodasLasIpJuntas_El601DesdeOtraIpEs429ConRetryAfter()
    {
        var token = TokenAvisoImpuesto.Emitir(Guid.NewGuid(), AvisosDePrueba.Llave);
        var ips = LimitesAvisosImpuesto.GlobalesPorMinuto / LimitesAvisosImpuesto.PorIpPorMinuto;
        for (var ip = 1; ip <= ips; ip++)
        {
            for (var i = 0; i < LimitesAvisosImpuesto.PorIpPorMinuto; i++)
                (await Post(token, AvisosDePrueba.Cuerpo("ext-x", 1), ip: Ip(ip))).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, $"ip {ip}, petición {i + 1}");
        }

        var rechazada = await Post(token, AvisosDePrueba.Cuerpo("ext-x", 1), ip: Ip(ips + 1));

        rechazada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "la IP nueva tiene cupo propio pero el global se agotó");
        rechazada.Headers.RetryAfter.Should().NotBeNull();
        (await rechazada.Content.ReadAsByteArrayAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task L1_TokenConFormaPeroHmacInvalido_NoConsumeElGlobal_SoloElCupoPorIp()
    {
        var falso = TokenAvisoImpuesto.Emitir(Guid.NewGuid(), System.Text.Encoding.UTF8.GetBytes("otra-llave-de-prueba-0123456789-abcdefgh"));
        TokenAvisoImpuesto.TieneForma(falso).Should().BeTrue("tiene la forma, solo el HMAC no coincide");
        var ips = LimitesAvisosImpuesto.GlobalesPorMinuto / LimitesAvisosImpuesto.PorIpPorMinuto;
        for (var ip = 1; ip <= ips; ip++)
        {
            for (var i = 0; i < LimitesAvisosImpuesto.PorIpPorMinuto; i++)
                (await Post(falso, AvisosDePrueba.Cuerpo("ext-x", 1), ip: Ip(ip))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"ip {ip}, petición {i + 1}");
        }

        (await Post(falso, AvisosDePrueba.Cuerpo("ext-x", 1), ip: Ip(1))).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests, "el token falso sí consume el cupo por IP");

        var legitimo = await Post(TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave), AvisosDePrueba.Cuerpo("ext-1", 1), ip: Ip(ips + 1));
        legitimo.StatusCode.Should().Be(HttpStatusCode.OK, "600 tokens falsos no agotan el global del aviso legítimo");
    }

    [Fact]
    public async Task L2_FallaInesperada_503SinCuerpo_NuncaLaDeveloperExceptionPage()
    {
        _secretos.Lanzar = new InvalidOperationException("detalle-interno-que-no-sale");

        var respuesta = await Post(TokenAvisoImpuesto.Emitir(Empresa, AvisosDePrueba.Llave), AvisosDePrueba.Cuerpo("ext-1", 1));

        respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await respuesta.Content.ReadAsByteArrayAsync(Ct)).Should().BeEmpty();
    }
}
