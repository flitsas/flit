using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Épica #12678 (sin HU nueva; engancha el permiso de la HU #13724 y el interruptor de la HU #13741).
/// Redactor LLM propio del producto: llave <c>Diagnostico:Anthropic</c>, simulado solo en LOCAL/DEV, y plantilla si el modelo no responde.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Redactor
/// </code>
/// </example>
public sealed class RedactorLlmTests
{
    private const string Llave = "llave-diagnostico";

    private static PedidoRedaccion Pedido(string veredicto = "VIABLE", string placa = "ABC123", bool suspendida = false) =>
        new(veredicto, [new ReglaDisparada("SIMIT_ALTO", "SIMIT_VEHICULO", "BLOQUEA", 150_000)], new DatosBien(placa, "AUTOMOVIL", "MARCA", "LINEA", "2020", "PARTICULAR"), suspendida);

    [Fact]
    public async Task ConLlave_ElCuerpoSoloLlevaTexto_YLaLlaveVaEnElEncabezado()
    {
        var handler = new Captura(CuerpoModelo("end_turn", "El dictamen del bien es VIABLE."));
        var proveedor = Anthropic(handler, maxTokens: 1234, modelo: "claude-haiku-4-5");

        var salida = await proveedor.RedactarAsync(PromptRedactor.Construir(Pedido()), CancellationToken.None);

        salida.TipoFallo.Should().BeNull();
        salida.Texto.Should().Be("El dictamen del bien es VIABLE.");
        salida.Modelo.Should().Be("claude-haiku-4-5");
        handler.Llave.Should().Be(Llave);
        handler.Version.Should().Be("2023-06-01");
        using var doc = JsonDocument.Parse(handler.Pedido!);
        var raiz = doc.RootElement;
        raiz.GetProperty("model").GetString().Should().Be("claude-haiku-4-5");
        raiz.GetProperty("max_tokens").GetInt32().Should().Be(1234);
        var content = raiz.GetProperty("messages")[0].GetProperty("content");
        content.GetArrayLength().Should().Be(1);
        content[0].GetProperty("type").GetString().Should().Be("text");
        handler.Pedido.Should().NotContain(Llave);
        handler.Pedido.Should().NotContain("base64");
        content.ToString().Should().NotContain("image").And.NotContain("document");
    }

    [Fact]
    public async Task RespuestaCortada_QuedaIlegible_YElMensajeNoTraeLaLlave()
    {
        var proveedor = Anthropic(new Captura(CuerpoModelo("max_tokens", "El dictamen")));

        var salida = await proveedor.RedactarAsync("prompt", CancellationToken.None);

        salida.TipoFallo.Should().Be(TipoFalloRedactor.Ilegible);
        salida.Mensaje.Should().Contain("límite de tokens");
        salida.Mensaje.Should().NotContain(Llave);
    }

    [Fact]
    public async Task RechazoDelModelo_QuedaIlegible()
    {
        var proveedor = Anthropic(new Captura(CuerpoModelo("refusal", "")));

        var salida = await proveedor.RedactarAsync("prompt", CancellationToken.None);

        salida.TipoFallo.Should().Be(TipoFalloRedactor.Ilegible);
        salida.Mensaje.Should().NotContain(Llave);
    }

    [Fact]
    public async Task Respuesta429_EsRateLimit_YConservaRetryAfter()
    {
        var respuesta = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        respuesta.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
        var proveedor = Anthropic(new Captura(respuesta));

        var salida = await proveedor.RedactarAsync("prompt", CancellationToken.None);

        salida.TipoFallo.Should().Be(TipoFalloRedactor.RateLimit);
        salida.ReintentarEn.Should().Be(TimeSpan.FromSeconds(2));
        salida.Mensaje.Should().NotContain(Llave);
    }

    [Fact]
    public async Task Respuesta5xx_EsHttp5xx_SinLaLlave()
    {
        var proveedor = Anthropic(new Captura(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(Llave, Encoding.UTF8, "text/plain"),
        }));

        var salida = await proveedor.RedactarAsync("prompt", CancellationToken.None);

        salida.TipoFallo.Should().Be(TipoFalloRedactor.Http5xx);
        salida.Mensaje.Should().Contain("503");
        salida.Mensaje.Should().NotContain(Llave);
    }

    [Fact]
    public async Task TiempoAgotado_EsTimeout()
    {
        var proveedor = Anthropic(new Cuelga(), timeoutSegundos: 1);

        var salida = await proveedor.RedactarAsync("prompt", CancellationToken.None);

        salida.TipoFallo.Should().Be(TipoFalloRedactor.Timeout);
    }

    [Fact]
    public async Task CancelacionDelLlamador_NoSeConvierteEnTimeout()
    {
        var proveedor = Anthropic(new Cuelga(), timeoutSegundos: 30);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var acto = () => proveedor.RedactarAsync("prompt", cts.Token);

        await acto.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("ABC123", null, "VIABLE")]
    [InlineData("ABC12F", TipoFalloRedactor.Red, null)]
    [InlineData("ABC12T", TipoFalloRedactor.Timeout, null)]
    public async Task Simulado_ElUltimoCaracterDeLaPlacaEligeElEscenario(string placa, TipoFalloRedactor? fallo, string? contiene)
    {
        var salida = await new ProveedorRedactorSimulado().RedactarAsync(PromptRedactor.Construir(Pedido(placa: placa)), CancellationToken.None);

        salida.TipoFallo.Should().Be(fallo);
        salida.Modelo.Should().Be("simulado");
        if (contiene is not null)
        {
            salida.Texto.Should().Contain(contiene);
            salida.Texto.Should().NotContain("BLOQUEADO");
        }
    }

    [Fact]
    public async Task Simulado_PlacaTerminadaEnX_DevuelveElVeredictoContrario()
    {
        var salida = await new ProveedorRedactorSimulado().RedactarAsync(PromptRedactor.Construir(Pedido(placa: "ABC12X")), CancellationToken.None);

        salida.TipoFallo.Should().BeNull();
        salida.Texto.Should().Contain("BLOQUEADO");
        salida.Texto.Should().NotContain("VIABLE");
    }

    [Fact]
    public void Prompt_SoloSerializaVeredictoReglasYBien()
    {
        var prompt = PromptRedactor.Construir(Pedido());
        using var doc = JsonDocument.Parse(prompt[prompt.IndexOf('{')..]);
        var raiz = doc.RootElement;

        raiz.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["veredicto", "reglas", "bien"]);
        raiz.GetProperty("reglas")[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["codigo", "fuente", "efecto", "monto_pesos"]);
        raiz.GetProperty("bien").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["placa", "clase", "marca", "linea", "modelo", "servicio"]);
        Regex.IsMatch(prompt, @"\bBLOQUEADO\b").Should().BeFalse();
        prompt.Should().NotContain("documento").And.NotContain("nombre").And.NotContain("propietario").And.NotContain("locatario");
    }

    [Fact]
    public void PromptDeBloqueado_NoMencionaElVeredictoContrario()
    {
        var prompt = PromptRedactor.Construir(Pedido(veredicto: "BLOQUEADO"));

        Regex.IsMatch(prompt, @"\bVIABLE\b").Should().BeFalse();
    }

    [Fact]
    public async Task TextoAlineado_QuedaCompletadoPorElLlm()
    {
        var proveedor = new ProveedorFijo(Salida("El dictamen del bien es VIABLE."));
        var servicio = Servicio(proveedor);

        var resultado = await servicio.RedactarAsync(Pedido(), CancellationToken.None);

        resultado.RedactadoPor.Should().Be("LLM");
        resultado.EstadoRedaccion.Should().Be("COMPLETADA");
        resultado.Texto.Should().Be("El dictamen del bien es VIABLE.");
        resultado.Intentos.Should().Be(1);
        resultado.TipoFallo.Should().BeNull();
        proveedor.Llamadas.Should().Be(1);
    }

    [Fact]
    public async Task TextoQueContradice_SeDescarta_YElSegundoIntentoValidoCompleta()
    {
        var proveedor = new ProveedorFijo(
            Salida("El dictamen del bien es BLOQUEADO."),
            Salida("El dictamen del bien es VIABLE."));
        var servicio = Servicio(proveedor, reintentos: 1);

        var resultado = await servicio.RedactarAsync(Pedido(), CancellationToken.None);

        resultado.RedactadoPor.Should().Be("LLM");
        resultado.EstadoRedaccion.Should().Be("COMPLETADA");
        resultado.Texto.Should().Be("El dictamen del bien es VIABLE.");
        resultado.Intentos.Should().Be(2);
        resultado.Texto.Should().NotContain("BLOQUEADO");
    }

    [Fact]
    public async Task SiSigueContradiciendo_SaleLaPlantilla()
    {
        var proveedor = new ProveedorFijo(
            Salida("El dictamen del bien es BLOQUEADO."),
            Salida("Sigue BLOQUEADO."));
        var servicio = Servicio(proveedor, reintentos: 1);

        var resultado = await servicio.RedactarAsync(Pedido(), CancellationToken.None);

        resultado.RedactadoPor.Should().Be("PLANTILLA");
        resultado.EstadoRedaccion.Should().Be("PENDIENTE");
        resultado.Texto.Should().StartWith("Redacción IA pendiente");
        resultado.Texto.Should().Contain("VIABLE").And.Contain("SIMIT_ALTO").And.Contain("ABC123");
        resultado.Texto.Should().NotContain("BLOQUEADO");
        resultado.Intentos.Should().Be(2);
        resultado.TipoFallo.Should().BeNull();
    }

    [Fact]
    public async Task FalloTecnico_NoSeReintenta_YSaleLaPlantillaConElTipo()
    {
        var proveedor = new ProveedorFijo(new SalidaModelo(null, "claude-haiku-4-5", TipoFalloRedactor.Timeout, "sin respuesta", TimeSpan.FromSeconds(5)));
        var servicio = Servicio(proveedor, reintentos: 1);

        var resultado = await servicio.RedactarAsync(Pedido(), CancellationToken.None);

        resultado.RedactadoPor.Should().Be("PLANTILLA");
        resultado.EstadoRedaccion.Should().Be("PENDIENTE");
        resultado.TipoFallo.Should().Be(TipoFalloRedactor.Timeout);
        resultado.ReintentarEn.Should().Be(TimeSpan.FromSeconds(5));
        resultado.Intentos.Should().Be(1);
        proveedor.Llamadas.Should().Be(1);
        resultado.Texto.Should().StartWith("Redacción IA pendiente");
    }

    [Fact]
    public async Task RedaccionSuspendida_NoLlamaAlProveedor()
    {
        var proveedor = new ProveedorFijo();
        var servicio = Servicio(proveedor);

        var resultado = await servicio.RedactarAsync(Pedido(suspendida: true), CancellationToken.None);

        proveedor.Llamadas.Should().Be(0);
        resultado.RedactadoPor.Should().Be("PLANTILLA");
        resultado.EstadoRedaccion.Should().Be("PENDIENTE");
        resultado.Intentos.Should().Be(0);
        resultado.TipoFallo.Should().BeNull();
        resultado.Texto.Should().StartWith("Redacción IA pendiente");
    }

    [Fact]
    public async Task SinProveedor_SaleLaPlantillaNoConfigurado_SinLlamar()
    {
        var proveedor = new ProveedorRedactorNoConfigurado();
        var servicio = Servicio(proveedor);

        var resultado = await servicio.RedactarAsync(Pedido(), CancellationToken.None);

        proveedor.Configurado.Should().BeFalse();
        resultado.RedactadoPor.Should().Be("PLANTILLA");
        resultado.EstadoRedaccion.Should().Be("PENDIENTE");
        resultado.TipoFallo.Should().Be(TipoFalloRedactor.NoConfigurado);
        resultado.Intentos.Should().Be(0);
        resultado.Modelo.Should().Be("no-configurado");
    }

    [Theory]
    [InlineData("LOCAL", "", "true", "", typeof(ProveedorRedactorSimulado))]
    [InlineData("DEV", "", "", "true", typeof(ProveedorRedactorSimulado))]
    [InlineData("LOCAL", "llave-real", "true", "", typeof(ProveedorRedactorAnthropic))]
    [InlineData("QA", "", "true", "true", typeof(ProveedorRedactorNoConfigurado))]
    [InlineData("PDN", "", "true", "true", typeof(ProveedorRedactorNoConfigurado))]
    [InlineData("LOCAL", "", "false", "false", typeof(ProveedorRedactorNoConfigurado))]
    [InlineData("", "", "true", "true", typeof(ProveedorRedactorNoConfigurado))]
    public void Registro_LaLlaveGana_YElSimuladoSoloExisteEnLocalODev(string ambiente, string apiKey, string simulado, string simulacionFuentes, Type tipo)
    {
        var proveedor = Resolver(new Dictionary<string, string?>
        {
            ["Diagnostico:Ambiente"] = ambiente,
            ["Diagnostico:Anthropic:ApiKey"] = apiKey,
            ["Diagnostico:Anthropic:Simulado"] = simulado,
            ["Diagnostico:Simulacion:Habilitada"] = simulacionFuentes,
        });

        proveedor.Should().BeOfType(tipo);
    }

    private static ProveedorRedactorAnthropic Anthropic(HttpMessageHandler handler, int maxTokens = 2000, string modelo = "claude-haiku-4-5", int timeoutSegundos = 30) =>
        new(new HttpClient(handler), Options.Create(new OpcionesAnthropic
        {
            ApiKey = Llave,
            Modelo = modelo,
            MaxTokens = maxTokens,
            TimeoutSegundos = timeoutSegundos,
        }));

    private static ServicioRedactor Servicio(IProveedorRedactor proveedor, int reintentos = 1) =>
        new(proveedor, Options.Create(new OpcionesAnthropic { ReintentosContradiccion = reintentos, Modelo = "claude-haiku-4-5" }));

    private static SalidaModelo Salida(string texto) => new(texto, "fijo");

    private static IProveedorRedactor Resolver(Dictionary<string, string?> valores)
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var servicios = new ServiceCollection();
        servicios.AddRedactorDictamen(configuracion);
        return servicios.BuildServiceProvider().GetRequiredService<IProveedorRedactor>();
    }

    private static string CuerpoModelo(string stopReason, string texto) => JsonSerializer.Serialize(new
    {
        stop_reason = stopReason,
        content = new object[] { new { type = "thinking", thinking = "pensando" }, new { type = "text", text = texto } },
    });

    private sealed class Captura(HttpResponseMessage respuesta) : HttpMessageHandler
    {
        public string? Pedido { get; private set; }
        public string? Llave { get; private set; }
        public string? Version { get; private set; }

        public Captura(string cuerpo) : this(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
        })
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedido = await request.Content!.ReadAsStringAsync(cancellationToken);
            Llave = request.Headers.GetValues("x-api-key").Single();
            Version = request.Headers.GetValues("anthropic-version").Single();
            return respuesta;
        }
    }

    private sealed class Cuelga : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("la petición no debía continuar");
        }
    }

    private sealed class ProveedorFijo : IProveedorRedactor
    {
        private readonly Queue<SalidaModelo> _salidas;
        public int Llamadas { get; private set; }
        public bool Configurado => true;
        public string Modelo => "fijo";

        public ProveedorFijo(params SalidaModelo[] salidas) => _salidas = new Queue<SalidaModelo>(salidas);

        public Task<SalidaModelo> RedactarAsync(string prompt, CancellationToken ct)
        {
            Llamadas++;
            return Task.FromResult(_salidas.Dequeue());
        }
    }
}
