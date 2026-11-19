using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen.Credencial;

/// <summary>
/// ADR-0091 §3.2, SP-12, SP-14(a), SP-17: <see cref="ProveedorRedactorAnthropic"/> toma la llave y el modelo de la credencial resuelta
/// en cada llamada; <c>BaseUrl</c> sigue local. «No configurado» saca el dictamen de la cola, un fallo técnico de core-api queda como
/// <see cref="TipoFalloRedactor.Red"/> (reintento acotado) y el tope local por minuto da <see cref="TipoFalloRedactor.RateLimit"/>.
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --sin-bd --filter "FullyQualifiedName~ProveedorRedactorPlataformaTests"
/// </code>
/// </example>
public sealed class ProveedorRedactorPlataformaTests
{
    private const string Centinela = "sk-ant-test-CENTINELA";

    private static PedidoRedaccion Pedido() =>
        new("VIABLE", [new ReglaDisparada("SIMIT_ALTO", "SIMIT_VEHICULO", "BLOQUEA", 150_000)], new DatosBien("ABC123", "AUTOMOVIL", "MARCA", "LINEA", "2020", "PARTICULAR"), false);

    private static ProveedorRedactorAnthropic Proveedor(HttpMessageHandler handler, IFuenteCredencialRedactor fuente, TopeLlamadasRedactor? tope = null) =>
        new(new HttpClient(handler), Options.Create(new OpcionesAnthropic { BaseUrl = "https://api.anthropic.com", ApiKey = string.Empty, Modelo = "modelo-local" }),
            fuente, tope ?? TopeLlamadasRedactor.SinTope());

    [Fact]
    public async Task LlaveYModeloSalenDeLaCredencial_YLaUrlEsLaLocal()
    {
        var http = new Anthropic();
        var proveedor = Proveedor(http, new FuenteFija(new CredencialRedactor("anthropic", "claude-haiku-4-5", Centinela, 7)));

        var salida = await proveedor.RedactarAsync(PromptRedactor.Construir(Pedido()), TestContext.Current.CancellationToken);

        salida.TipoFallo.Should().BeNull();
        salida.Modelo.Should().Be("claude-haiku-4-5");
        http.Llamadas.Should().Be(1);
        http.Uri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        http.Llave.Should().Be(Centinela);
        using var doc = JsonDocument.Parse(http.Cuerpo!);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["model", "max_tokens", "messages"]);
        doc.RootElement.GetProperty("model").GetString().Should().Be("claude-haiku-4-5");
        http.Cuerpo.Should().NotContain(Centinela);
    }

    [Fact]
    public async Task NoConfigurado_NoLlamaAlProveedor_YSaleDeLaCola()
    {
        var http = new Anthropic();
        var proveedor = Proveedor(http, new FuenteFija(FallaCredencial.NoConfigurado));

        var salida = await proveedor.RedactarAsync("prompt", TestContext.Current.CancellationToken);
        var resultado = await Servicio(proveedor).RedactarAsync(Pedido(), TestContext.Current.CancellationToken);

        salida.TipoFallo.Should().Be(TipoFalloRedactor.NoConfigurado);
        http.Llamadas.Should().Be(0);
        resultado.RedactadoPor.Should().Be("PLANTILLA");
        resultado.TipoFallo.Should().Be(TipoFalloRedactor.NoConfigurado);
        EmisorDictamen.DisponibleDesde(resultado, DateTimeOffset.UnixEpoch).Should().BeNull("sin proveedor el dictamen sale de la cola");
    }

    [Fact]
    public async Task SP12_FalloTecnicoDeCoreApi_EsRed_YVuelveALaCola()
    {
        var http = new Anthropic();
        var proveedor = Proveedor(http, new FuenteFija(FallaCredencial.Tecnica));

        var salida = await proveedor.RedactarAsync("prompt", TestContext.Current.CancellationToken);
        var resultado = await Servicio(proveedor).RedactarAsync(Pedido(), TestContext.Current.CancellationToken);

        salida.TipoFallo.Should().Be(TipoFalloRedactor.Red);
        http.Llamadas.Should().Be(0);
        resultado.TipoFallo.Should().Be(TipoFalloRedactor.Red);
        EmisorDictamen.DisponibleDesde(resultado, DateTimeOffset.UnixEpoch).Should().NotBeNull("un fallo técnico se reintenta (H8)");
    }

    [Fact]
    public async Task SP17_PasadoElTopePorMinuto_EsRateLimitReintentable_SinLlamar()
    {
        var reloj = new RelojMovil(DateTimeOffset.Parse("2026-11-18T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var http = new Anthropic();
        var proveedor = Proveedor(http, new FuenteFija(new CredencialRedactor("anthropic", "claude-haiku-4-5", Centinela, 7)), new TopeLlamadasRedactor(2, reloj));
        var ct = TestContext.Current.CancellationToken;

        (await proveedor.RedactarAsync("prompt", ct)).TipoFallo.Should().BeNull();
        reloj.Avanzar(TimeSpan.FromSeconds(10));
        (await proveedor.RedactarAsync("prompt", ct)).TipoFallo.Should().BeNull();
        var tercera = await proveedor.RedactarAsync("prompt", ct);

        tercera.TipoFallo.Should().Be(TipoFalloRedactor.RateLimit);
        tercera.ReintentarEn.Should().Be(TimeSpan.FromSeconds(50));
        http.Llamadas.Should().Be(2);
        var resultado = new ResultadoRedaccion("t", "PLANTILLA", "PENDIENTE", 1, "m", tercera.TipoFallo, tercera.ReintentarEn);
        EmisorDictamen.DisponibleDesde(resultado, DateTimeOffset.UnixEpoch).Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(50));

        reloj.Avanzar(TimeSpan.FromSeconds(51));
        (await proveedor.RedactarAsync("prompt", ct)).TipoFallo.Should().BeNull();
        http.Llamadas.Should().Be(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Un401O403DelProveedor_InvalidaLaCredencial_YQuedaIlegible(HttpStatusCode codigo)
    {
        var fuente = new FuenteFija(new CredencialRedactor("anthropic", "claude-haiku-4-5", Centinela, 7));
        var proveedor = Proveedor(new Anthropic(codigo), fuente);

        var salida = await proveedor.RedactarAsync("prompt", TestContext.Current.CancellationToken);

        salida.TipoFallo.Should().Be(TipoFalloRedactor.Ilegible);
        salida.Mensaje.Should().NotContain(Centinela);
        fuente.Invalidaciones.Should().Be(1);
    }

    [Fact]
    public async Task SP14_ModeloFueraDeLaAllowlist_CeroLlamadasAAnthropic()
    {
        var v = VectorIa.Cargar();
        using var privada = v.Privada();
        using var publica = v.Publica();
        var reloj = new RelojMovil(v.Emitida);
        await using var core = await CoreApiIaFalso.IniciarAsync();
        core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, publica, reloj.GetUtcNow(), modelo: "claude-opus-4-1"));
        using var fuente = new FuenteCredencialPlataforma(core.Cliente, new ClavesPrivadasIa(v.Kid, privada),
            Options.Create(new OpcionesIa { Origen = OpcionesIa.OrigenPlataforma, Kid = v.Kid }), reloj, new LogsIa().Logger<FuenteCredencialPlataforma>());
        var http = new Anthropic();

        var salida = await Proveedor(http, fuente).RedactarAsync("prompt", TestContext.Current.CancellationToken);

        salida.TipoFallo.Should().Be(TipoFalloRedactor.NoConfigurado);
        http.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task FuenteLocal_EnvuelveLaLlaveYElModeloDeOpcionesAnthropic()
    {
        var local = new FuenteCredencialLocal(Options.Create(new OpcionesAnthropic { ApiKey = Centinela, Modelo = "claude-haiku-4-5" }));
        var sinLlave = new FuenteCredencialLocal(Options.Create(new OpcionesAnthropic { ApiKey = " " }));

        var r = await local.ObtenerAsync(TestContext.Current.CancellationToken);

        r.Falla.Should().Be(FallaCredencial.Ninguna);
        r.Credencial!.ApiKey.Should().Be(Centinela);
        r.Credencial.Modelo.Should().Be("claude-haiku-4-5");
        (await sinLlave.ObtenerAsync(TestContext.Current.CancellationToken)).Falla.Should().Be(FallaCredencial.NoConfigurado);
    }

    private static ServicioRedactor Servicio(IProveedorRedactor proveedor) =>
        new(proveedor, Options.Create(new OpcionesAnthropic { ReintentosContradiccion = 1 }));

    private sealed class FuenteFija : IFuenteCredencialRedactor
    {
        private readonly ResultadoCredencial _resultado;

        public FuenteFija(CredencialRedactor credencial) => _resultado = ResultadoCredencial.Con(credencial);

        public FuenteFija(FallaCredencial falla) => _resultado = falla == FallaCredencial.Tecnica ? ResultadoCredencial.Tecnica : ResultadoCredencial.NoConfigurado;

        public int Invalidaciones { get; private set; }

        public Task<ResultadoCredencial> ObtenerAsync(CancellationToken ct) => Task.FromResult(_resultado);

        public void Invalidar() => Invalidaciones++;
    }

    private sealed class Anthropic(HttpStatusCode codigo = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Llamadas { get; private set; }

        public Uri? Uri { get; private set; }

        public string? Llave { get; private set; }

        public string? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            Uri = request.RequestUri;
            Llave = request.Headers.TryGetValues("x-api-key", out var v) ? v.Single() : null;
            Cuerpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            var json = JsonSerializer.Serialize(new { stop_reason = "end_turn", content = new object[] { new { type = "text", text = "El dictamen del bien es VIABLE." } } });
            return new HttpResponseMessage(codigo) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

/// <summary>SP-17: tope local de llamadas por minuto al proveedor (ventana deslizante de 60 s sobre el <see cref="TimeProvider"/>).</summary>
public sealed class TopeLlamadasRedactorTests
{
    [Fact]
    public void LaVentanaEsDeslizante_YLaEsperaEsHastaQueSaleLaMasVieja()
    {
        var reloj = new RelojMovil(DateTimeOffset.UnixEpoch);
        var tope = new TopeLlamadasRedactor(3, reloj);

        tope.IntentarTomar(out _).Should().BeTrue();
        reloj.Avanzar(TimeSpan.FromSeconds(20));
        tope.IntentarTomar(out _).Should().BeTrue();
        tope.IntentarTomar(out _).Should().BeTrue();
        tope.IntentarTomar(out var espera).Should().BeFalse();
        espera.Should().Be(TimeSpan.FromSeconds(40));

        reloj.Avanzar(TimeSpan.FromSeconds(40));
        tope.IntentarTomar(out _).Should().BeTrue("la primera ya salió de la ventana");
        tope.IntentarTomar(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnTopeNoPositivo_EsElDefectoDe30(int valor)
    {
        var tope = new TopeLlamadasRedactor(valor, new RelojMovil(DateTimeOffset.UnixEpoch));

        Enumerable.Range(0, 30).Select(i => tope.IntentarTomar(out _)).Should().OnlyContain(x => x);
        tope.IntentarTomar(out _).Should().BeFalse();
        new OpcionesIa().MaxLlamadasPorMinuto.Should().Be(30);
    }

    [Fact]
    public void SinTope_NoLimita()
    {
        var tope = TopeLlamadasRedactor.SinTope();

        Enumerable.Range(0, 500).Select(i => tope.IntentarTomar(out _)).Should().OnlyContain(x => x);
    }
}
