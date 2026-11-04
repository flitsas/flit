using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Abuso;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14036 (revisión de seguridad A2, M3, M4) — IP real por saltos de confianza, errores sin detalle (también en
/// Development), captcha «deshabilitado» con doble seguro y tope global del cuerpo JSON. Host real sobre TestServer; no
/// toca la base (nada de esto llega a ella), así que corre aun sin Postgres.
/// </summary>
public sealed class AntiAbusoHttpTests
{
    private const string Peer = "X-Test-Peer";

    // ── A2: X-Forwarded-For con saltos de confianza ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinConfiarEnXForwardedFor_LaIpEsLaDelPeer_AunQueLlegueLaCabecera()
    {
        await using var host = await HostLigero.IniciarAsync();

        (await host.IpAsync("172.18.0.5", "203.0.113.9")).Should().Be("172.18.0.5");
    }

    [Fact]
    public async Task ConRedesDeConfianzaPorDefecto_TomaLaIpDelCiudadano()
    {
        await using var host = await HostLigero.IniciarAsync(("Abuso:ConfiarEnXForwardedFor", "true"));

        (await host.IpAsync("172.18.0.5", "203.0.113.9")).Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task DetrasDeNginxNextYGateway_RecorreLosSaltosInternosYLlegaAlCiudadano()
    {
        await using var host = await HostLigero.IniciarAsync(("Abuso:ConfiarEnXForwardedFor", "true"));

        // nginx puso al ciudadano; Next sumó a nginx y el gateway sumó a Next. El peer es el gateway.
        (await host.IpAsync("172.18.0.4", "203.0.113.9, 172.18.0.2, 172.18.0.3")).Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task UnXForwardedForFalseadoPorElCliente_NoReemplazaLaIpQueVioElProxyDeConfianza()
    {
        await using var host = await HostLigero.IniciarAsync(("Abuso:ConfiarEnXForwardedFor", "true"));

        // El cliente mandó «1.2.3.4» y el proxy (que no sobrescribió la cabecera) sumó la IP real al final.
        (await host.IpAsync("172.18.0.5", "1.2.3.4, 203.0.113.9")).Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task UnPeerQueNoEsDeConfianza_NoPuedeDecirQuienEs()
    {
        await using var host = await HostLigero.IniciarAsync(("Abuso:ConfiarEnXForwardedFor", "true"));

        (await host.IpAsync("203.0.113.50", "1.2.3.4")).Should().Be("203.0.113.50");
    }

    [Fact]
    public async Task SinRedesDeConfianza_LaCabeceraSeIgnoraAunConElInterruptorEncendido()
    {
        await using var host = await HostLigero.IniciarAsync(("Abuso:ConfiarEnXForwardedFor", "true"), ("Abuso:RedesDeConfianza:0", ""));

        (await host.IpAsync("172.18.0.5", "203.0.113.9")).Should().Be("172.18.0.5");
    }

    [Fact]
    public async Task LasRedesDeConfianzaSeConfiguran_YLasPrivadasPorDefectoYaNoValen()
    {
        await using var host = await HostLigero.IniciarAsync(("Abuso:ConfiarEnXForwardedFor", "true"), ("Abuso:RedesDeConfianza:0", "100.64.0.0/10"));

        (await host.IpAsync("100.64.1.1", "203.0.113.9")).Should().Be("203.0.113.9");
        (await host.IpAsync("172.18.0.5", "203.0.113.9")).Should().Be("172.18.0.5", "10.x/172.16.x ya no están configuradas");
    }

    [Fact]
    public async Task ElTopeDeSaltos_LimitaCuantosSaltosDeConfianzaSeRecorren()
    {
        await using var host = await HostLigero.IniciarAsync(("Abuso:ConfiarEnXForwardedFor", "true"), ("Abuso:SaltosDeConfianza", "1"));

        // Con un salto solo se pasa del peer al último de la cadena.
        (await host.IpAsync("172.18.0.4", "203.0.113.9, 172.18.0.2, 172.18.0.3")).Should().Be("172.18.0.3");
    }

    [Fact]
    public void UnaRedInvalida_ImpideArrancar()
    {
        var opciones = new ForwardedHeadersOptions();

        var act = () => ForwardedHeadersConfianza.Aplicar(opciones, new AbusoOptions(), ["no-es-una-red"]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Abuso__RedesDeConfianza*");
    }

    // ── M3: errores sin detalle ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnaExcepcionNoManejada_RespondeProblemJsonConCodigoEstable_SinTrazasNiRutas(string ambiente)
    {
        await using var host = await HostLigero.IniciarAsync(ambiente, []);

        using var respuesta = await host.Http.GetAsync("/_boom", TestContext.Current.CancellationToken);

        respuesta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var texto = await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(texto);
        json.RootElement.GetProperty("code").GetString().Should().Be(AntiAbusoHttp.CodigoErrorInterno);
        texto.Should().NotContainAny("detalle-secreto", "InvalidOperationException", "StackTrace", ".cs", "at Flit", "C:\\", "1020456789");
    }

    // ── M4: tope global del cuerpo JSON ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnCuerpoJsonMasGrandeQueElTope_Responde413SinLlegarAlEndpoint()
    {
        await using var host = await HostLigero.IniciarAsync();

        using var respuesta = await host.Http.PostAsync("/_eco", Json(70 * 1024), TestContext.Current.CancellationToken);

        respuesta.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await HostLigero.CodigoAsync(respuesta)).Should().Be(AntiAbusoHttp.CodigoCuerpoGrande);
        host.EcoLlamado.Should().BeFalse();
    }

    [Fact]
    public async Task UnCuerpoJsonBajoElTope_PasaAlEndpoint()
    {
        await using var host = await HostLigero.IniciarAsync();

        using var respuesta = await host.Http.PostAsync("/_eco", Json(10 * 1024), TestContext.Current.CancellationToken);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        host.EcoLlamado.Should().BeTrue();
    }

    [Fact]
    public async Task UnCuerpoSinContentLength_SeCortaAlPasarElTope()
    {
        await using var host = await HostLigero.IniciarAsync();
        var contenido = new StreamContent(new MemoryStream(new byte[70 * 1024]));
        contenido.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        contenido.Headers.ContentLength = null;
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/_eco") { Content = contenido };
        peticion.Headers.TransferEncodingChunked = true;

        using var respuesta = await host.Http.SendAsync(peticion, TestContext.Current.CancellationToken);

        respuesta.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await HostLigero.CodigoAsync(respuesta)).Should().Be(AntiAbusoHttp.CodigoCuerpoGrande);
    }

    [Fact]
    public async Task LasRutasDeArchivosQuedanFueraDelTopeJson()
    {
        await using var host = await HostLigero.IniciarAsync();

        using var adjunto = await host.Http.PostAsync("/_x/adjuntos", new ByteArrayContent(new byte[200 * 1024]), TestContext.Current.CancellationToken);
        using var comprobante = await host.Http.PostAsync("/_x/pago/comprobante", new ByteArrayContent(new byte[200 * 1024]), TestContext.Current.CancellationToken);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[200 * 1024]), "archivo", "a.pdf" } };
        using var multipart = await host.Http.PostAsync("/_eco", form, TestContext.Current.CancellationToken);

        adjunto.StatusCode.Should().Be(HttpStatusCode.OK);
        comprobante.StatusCode.Should().Be(HttpStatusCode.OK);
        multipart.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── S-M? captcha «deshabilitado» con doble seguro ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Development", false, false)]
    [InlineData("Production", true, false)]
    [InlineData("Development", true, true)]
    public void ElCaptchaDeshabilitado_SoloArrancaEnDevelopmentYConLaVariableExplicita(string ambiente, bool permitir, bool arranca)
    {
        var act = () => HostLigero.Construir(ambiente, [("Captcha:Proveedor", "deshabilitado"), ("Captcha:PermitirDeshabilitado", permitir ? "true" : "false")]);

        if (arranca)
            act.Should().NotThrow();
        else
            act.Should().Throw<InvalidOperationException>().WithMessage("*PermitirDeshabilitado*");
    }

    private static StringContent Json(int bytes) => new("\"" + new string('a', bytes) + "\"", Encoding.UTF8, "application/json");

    /// <summary>Host sin base de datos: las rutas <c>/_ip</c>, <c>/_boom</c>, <c>/_eco</c> y <c>/_x/*</c> existen solo para probar la tubería HTTP.</summary>
    private sealed class HostLigero : IAsyncDisposable
    {
        public WebApplication App { get; private set; } = null!;

        public HttpClient Http { get; private set; } = null!;

        public bool EcoLlamado { get; set; }

        public static async Task<HostLigero> IniciarAsync(params (string Clave, string Valor)[] ajustes) => await IniciarAsync("Production", ajustes);

        public static async Task<HostLigero> IniciarAsync(string ambiente, (string Clave, string Valor)[] ajustes)
        {
            var host = new HostLigero();
            host.App = Construir(ambiente, ajustes, b => b.Services.AddSingleton<IStartupFilter>(new Filtro(host)));
            await host.App.StartAsync();
            host.Http = host.App.GetTestClient();
            return host;
        }

        public static WebApplication Construir(string ambiente, (string Clave, string Valor)[] ajustes, Action<WebApplicationBuilder>? extra = null)
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Servicio"] = "Host=127.0.0.1;Port=1;Database=sin_base;Username=x;Password=x;Timeout=1",
                ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
                ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
                ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
                ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
                ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
                [ServicioSettings.GrpcPortKey] = "5990",
                ["Pii:ClaveHmac"] = PortalHost.ClaveHmac,
                ["Captcha:SecretKey"] = "secreto-de-captcha",
                ["Pii:Cifrado:Llaves:1"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            };
            foreach (var (clave, valor) in ajustes)
                settings[clave] = valor;

            return Program.Build([], b =>
            {
                b.Environment.EnvironmentName = ambiente;
                b.WebHost.UseTestServer();
                b.Configuration.AddInMemoryCollection(settings);
                extra?.Invoke(b);
            });
        }

        /// <summary>Devuelve la IP que el servicio ve para un peer y un X-Forwarded-For dados.</summary>
        public async Task<string> IpAsync(string peer, string? forwardedFor)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Get, "/_ip");
            peticion.Headers.Add(Peer, peer);
            if (forwardedFor is not null)
                peticion.Headers.Add("X-Forwarded-For", forwardedFor);
            using var respuesta = await Http.SendAsync(peticion, TestContext.Current.CancellationToken);
            return await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        public static async Task<string?> CodigoAsync(HttpResponseMessage r)
        {
            using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return json.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        }

        public async ValueTask DisposeAsync()
        {
            if (App is not null)
                await App.DisposeAsync();
        }

        /// <summary>Fija la IP del peer (TestServer no tiene una) y suma, detrás de toda la tubería de la app, las rutas de prueba.</summary>
        private sealed class Filtro(HostLigero origen) : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use(async (ctx, siguiente) =>
                {
                    if (ctx.Request.Headers.TryGetValue(Peer, out var peer))
                        ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
                    await siguiente(ctx);
                });
                next(app);
                app.Map("/_ip", b => b.Run(ctx => ctx.Response.WriteAsync(ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty)));
                app.Map("/_boom", b => b.Run(_ => throw new InvalidOperationException("detalle-secreto C:\\ruta\\Secreta.cs cédula 1020456789")));
                app.Map("/_eco", b => b.Run(async ctx =>
                {
                    origen.EcoLlamado = true;
                    await ctx.Request.Body.CopyToAsync(Stream.Null);
                }));
                app.Map("/_x", b => b.Run(async ctx => await ctx.Request.Body.CopyToAsync(Stream.Null)));
            };
        }
    }
}
