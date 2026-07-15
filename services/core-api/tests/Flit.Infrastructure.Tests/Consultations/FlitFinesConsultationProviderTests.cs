using System.Net;
using System.Text;
using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// FEATURE 05 (HU #10756) — proveedor de comparendos de la fuente interna.
/// La forma del JSON de estos tests es la REAL, verificada contra el API en vivo: payload plano
/// (sin el envoltorio value.value.data de Verifik) y campos numéricos como texto.
/// </summary>
public sealed class FlitFinesConsultationProviderTests
{
    private const string BaseUrlConStage = "https://api.example.com/pdn";

    private static FlitFinesConsultationProvider BuildProvider(
        string mode = "mock", HttpMessageHandler? handler = null)
    {
        var opts = new FlitRegistrationApiOptions { BaseUrl = BaseUrlConStage };
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = new Uri(opts.NormalizedBaseUrl);

        return new FlitFinesConsultationProvider(
            http,
            Options.Create(opts),
            Options.Create(new ConsultationProviderModeOptions { FlitFinesMode = mode }));
    }

    private static ConsultationContext Ctx(string? docType = "CC", string? docNumber = "1000000001") =>
        new(Guid.Empty, Guid.Empty, "flit_fines",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["owner_document_type"] = docType,
                ["owner_document_number"] = docNumber,
            });

    private static ConsultationCheck Check(ConsultationResult r, string key) =>
        r.Checks.Single(c => c.Key == key);

    [Fact]
    public void Key_EsFlitFines() =>
        BuildProvider().Key.Should().Be("flit_fines");

    [Fact]
    public async Task ConsultAsync_ModoMock_DevuelveGreenSinComparendos()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("{}");

        var result = await BuildProvider("mock", handler).ConsultAsync(Ctx(), ct);

        result.Overall.Should().Be("green");
        Check(result, FinesCheckFactory.KeyMultas).Status.Should().Be("ok");
        // El mock no debe tocar la red: protege el arranque en entornos sin salida.
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_LlamaLaRutaConservandoElStageDeLaUrlBase()
    {
        // AC1 + trampa del BaseUrl con path: si la ruta relativa llevara barra inicial o la base
        // no la conservara, la llamada se iría a la raíz y perdería /pdn → 404 en producción.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("{\"multas\":[],\"acuerdosPago\":[]}");

        await BuildProvider("real", handler).ConsultAsync(Ctx("CC", "1000000001"), ct);

        var uri = handler.LastRequest!.RequestUri!;
        uri.AbsolutePath.Should().Be("/pdn/api/v1/registration/simit");
        uri.Query.Should().Contain("documentType=CC").And.Contain("documentNumber=1000000001");
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_NoEnviaCabeceraDeAutorizacion()
    {
        // AC1: el API interno no exige credenciales.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("{\"multas\":[],\"acuerdosPago\":[]}");

        await BuildProvider("real", handler).ConsultAsync(Ctx(), ct);

        handler.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task ConsultAsync_ConMultasPendientes_DevuelveWarnNoFail()
    {
        // AC5 del Feature a nivel proveedor: los comparendos advierten, no bloquean.
        // Números como TEXTO, tal y como responde el API real.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler(
            """
            {"multas":[
              {"estadoComparendo":"Pendiente","valorPagar":"742730","numeroComparendo":"1"},
              {"estadoComparendo":"Pendiente","valorPagar":"200000","numeroComparendo":"2"}
            ],"acuerdosPago":[]}
            """);

        var result = await BuildProvider("real", handler).ConsultAsync(Ctx(), ct);

        var multas = Check(result, FinesCheckFactory.KeyMultas);
        multas.Status.Should().Be("warn");
        result.Overall.Should().Be("yellow");
        // Prueba que AllowReadingFromString convierte el texto a decimal y que la suma es correcta.
        multas.Message.Should().Contain("2 multa(s)").And.Contain("942");
    }

    [Fact]
    public async Task ConsultAsync_EstadosQueNoSonPendiente_NoCuentanComoComparendo()
    {
        // "Pendiente Curso" (sin deuda) y estado nulo son estados reales de la fuente y NO son
        // comparendos pendientes. Ampliar la coincidencia bloquearía trámites en la radicación.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler(
            """
            {"multas":[
              {"estadoComparendo":"Pendiente Curso","valorPagar":"0","numeroComparendo":"1"},
              {"estadoComparendo":null,"valorPagar":"580000","numeroComparendo":"2"},
              {"estadoComparendo":"Pagado","valorPagar":"0","numeroComparendo":"3"}
            ],"acuerdosPago":[]}
            """);

        var result = await BuildProvider("real", handler).ConsultAsync(Ctx(), ct);

        Check(result, FinesCheckFactory.KeyMultas).Status.Should().Be("ok");
        result.Overall.Should().Be("green");
    }

    [Fact]
    public async Task ConsultAsync_IgnoraLosAgregadosNoFiablesDeLaFuente()
    {
        // AC2: totalMultasPagar trae la CANTIDAD de multas (no el monto) y cantMultasPagar viene
        // en cero aun habiendo pendientes. El mapper debe calcular desde el detalle: si leyera los
        // agregados, aquí reportaría "$26 COP" de deuda.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler(
            """
            {"multas":[{"estadoComparendo":"Pendiente","valorPagar":"742730","numeroComparendo":"1"}],
             "acuerdosPago":[],
             "totalMultasPagar":"26","cantMultasPagar":"0","totalMultas":"11164943"}
            """);

        var result = await BuildProvider("real", handler).ConsultAsync(Ctx(), ct);

        var multas = Check(result, FinesCheckFactory.KeyMultas);
        multas.Message.Should().Contain("1 multa(s)").And.Contain("742");
        multas.Message.Should().NotContain("26 multa");
    }

    [Fact]
    public async Task ConsultAsync_ConAcuerdoDePago_DevuelveWarnConClaveDistintaDeMultas()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler(
            """{"multas":[],"acuerdosPago":[{"estado":"Acuerdo de pago","pendiente":"837716"}]}""");

        var result = await BuildProvider("real", handler).ConsultAsync(Ctx(), ct);

        Check(result, FinesCheckFactory.KeyAcuerdos).Status.Should().Be("warn");
        // El gate de radicación exige el sufijo _multas: el acuerdo no debe colarse como comparendo.
        FinesCheckFactory.KeyAcuerdos.Should().NotEndWith(FinesCheckFactory.KeyMultas);
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_404_DevuelveGreenSinRegistros()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("", HttpStatusCode.NotFound);

        var result = await BuildProvider("real", handler).ConsultAsync(Ctx(), ct);

        result.Overall.Should().Be("green");
        Check(result, FinesCheckFactory.KeyMultas).Status.Should().Be("ok");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ConsultAsync_ModoReal_RespuestaNoExitosa_DevuelveErrorSinLanzar(HttpStatusCode status)
    {
        // AC4: nunca lanza excepción de transporte al orquestador.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("", status);

        var result = await BuildProvider("real", handler).ConsultAsync(Ctx(), ct);

        result.Overall.Should().Be("red");
        result.Checks.Should().ContainSingle(c => c.Status == "error");
        result.Checks.Single().Message.Should().NotContainAny("http", "500", "502", "401");
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_JsonIlegible_DevuelveErrorSinLanzar()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("<html>gateway timeout</html>");

        var result = await BuildProvider("real", handler).ConsultAsync(Ctx(), ct);

        result.Overall.Should().Be("red");
        result.Checks.Should().ContainSingle(c => c.Status == "error");
    }

    [Theory]
    [InlineData(null, "1000000001")]
    [InlineData("CC", null)]
    [InlineData("", "")]
    public async Task ConsultAsync_SinDocumentoCompleto_DevuelveUnknownSinLlamarAlApi(string? tipo, string? numero)
    {
        // AC5: dato ausente ⇒ unknown (no bloquea), y sin gastar una llamada.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("{}");

        var result = await BuildProvider("real", handler).ConsultAsync(Ctx(tipo, numero), ct);

        result.Overall.Should().Be("yellow");
        result.Checks.Should().ContainSingle(c => c.Status == "unknown");
        handler.LastRequest.Should().BeNull();
    }

    private sealed class CapturingHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }
}
