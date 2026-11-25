using System.Net;
using System.Text;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Platform.Sdk.IaConsumo;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// ADR-0090 — el redactor del dictamen deja una llamada registrada por cada petición a Anthropic, con el <c>usage</c> y
/// el ámbito <c>diagnostico/redactar_dictamen</c> (placa del bien) que abre <see cref="ServicioRedactor"/>; lo que
/// devuelve no cambia.
/// </summary>
public sealed class IaConsumoRedactorTests
{
    private sealed class RegistradorFalso : IIaConsumoRegistrador
    {
        public List<(IaLlamada Llamada, IaAmbitoActual? Ambito)> Llamadas { get; } = [];
        public IIaCallContext Contexto => IaCallContext.Instancia;
        public bool Activo => true;
        public bool DetalleActivo => false;
        public void Registrar(IaLlamada llamada) => Llamadas.Add((llamada, Contexto.Actual));
    }

    private sealed class Handler(Func<HttpResponseMessage> respuesta) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respuesta());
    }

    private static readonly Func<HttpResponseMessage> Ok = () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""
            {"id":"msg_3","model":"claude-haiku-4-5","stop_reason":"end_turn",
             "content":[{"type":"text","text":"El dictamen del bien es VIABLE."}],
             "usage":{"input_tokens":900,"output_tokens":80,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}
            """, Encoding.UTF8, "application/json"),
    };

    private static (ServicioRedactor Servicio, RegistradorFalso Reg) Redactor(Func<HttpResponseMessage> respuesta)
    {
        var reg = new RegistradorFalso();
        var opciones = Options.Create(new OpcionesAnthropic { ApiKey = "k", Modelo = "claude-haiku-4-5", MaxTokens = 2000, TimeoutSegundos = 30 });
        var proveedor = new ProveedorRedactorAnthropic(new HttpClient(new Handler(respuesta)), opciones, reg);
        return (new ServicioRedactor(proveedor, opciones), reg);
    }

    private static PedidoRedaccion Pedido() =>
        new("VIABLE", [new ReglaDisparada("SIMIT_ALTO", "SIMIT_VEHICULO", "BLOQUEA", 150_000)],
            new DatosBien("abc-123", "AUTOMOVIL", "MARCA", "LINEA", "2020", "PARTICULAR"), false);

    [Fact]
    public async Task Exito_RegistraTokensYElAmbitoDelDictamen()
    {
        var (servicio, reg) = Redactor(Ok);

        var r = await servicio.RedactarAsync(Pedido(), TestContext.Current.CancellationToken);

        r.RedactadoPor.Should().Be("LLM");
        var (llamada, ambito) = reg.Llamadas.Single();
        llamada.Resultado.Should().Be(IaResultados.Exito);
        llamada.TipoTareaPorDefecto.Should().Be(IaTiposTarea.Redaccion);
        llamada.TokensEntrada.Should().Be(900);
        llamada.TokensSalida.Should().Be(80);
        llamada.IdRespuestaProveedor.Should().Be("msg_3");
        ambito!.Ambito.Modulo.Should().Be(IaProcesosFlit.ModuloDiagnostico);
        ambito.Ambito.Proceso.Should().Be(IaProcesosFlit.RedactarDictamen);
        ambito.Ambito.Placa.Should().Be("abc-123", "se normaliza al armar la fila");
        IaCallContext.Instancia.Actual.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, IaResultados.RateLimit)]
    [InlineData(HttpStatusCode.ServiceUnavailable, IaResultados.ErrorProveedor)]
    public async Task NoExito_RegistraElResultadoYCaeEnLaPlantilla(HttpStatusCode status, string resultado)
    {
        var (servicio, reg) = Redactor(() => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"type":"error","error":{"type":"overloaded_error","message":"m"}}""", Encoding.UTF8, "application/json"),
        });

        var r = await servicio.RedactarAsync(Pedido(), TestContext.Current.CancellationToken);

        r.RedactadoPor.Should().Be("PLANTILLA");
        var llamada = reg.Llamadas.Single().Llamada;
        llamada.Resultado.Should().Be(resultado);
        llamada.HttpStatus.Should().Be((int)status);
        llamada.ErrorCodigo.Should().Be("overloaded_error");
    }
}
