using System.Net;
using System.Text;
using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// FEATURE 05 (HU #10757) — proveedor KYVERUM de comparendos para persona jurídica.
/// Arranca en mock: el proveedor no ha entregado credenciales ni especificación.
/// </summary>
public sealed class KyverumFinesConsultationProviderTests
{
    private static KyverumFinesConsultationProvider BuildProvider(
        string mode = "mock", HttpMessageHandler? handler = null, string apiKey = "")
    {
        var opts = new KyverumFinesOptions
        {
            BaseUrl = "https://runt.kyverum.com",
            InfractionPath = "/v1/comparendos:consultar",
            ApiKey = apiKey,
            AuthScheme = "Bearer",
        };
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = new Uri(opts.BaseUrl);

        return new KyverumFinesConsultationProvider(
            http,
            Options.Create(opts),
            Options.Create(new ConsultationProviderModeOptions { KyverumFinesMode = mode }));
    }

    private static ConsultationContext Ctx(string? docType = "NIT", string? docNumber = "900123456") =>
        new(Guid.Empty, Guid.Empty, "kyverum_fines",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["owner_document_type"] = docType,
                ["owner_document_number"] = docNumber,
            });

    [Fact]
    public void Key_EsKyverumFines() =>
        BuildProvider().Key.Should().Be("kyverum_fines");

    [Fact]
    public async Task ConsultAsync_ModoMockPorDefecto_NoLlamaLaRed()
    {
        // AC2 — protege el arranque sin credenciales: en el default no debe salir ni una request.
        // Es lo que permite que la demo funcione aunque el proveedor no entregue a tiempo.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("{}");
        var modosPorDefecto = new ConsultationProviderModeOptions();

        modosPorDefecto.KyverumFinesMode.Should().Be("mock");

        var provider = new KyverumFinesConsultationProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://runt.kyverum.com") },
            Options.Create(new KyverumFinesOptions()),
            Options.Create(modosPorDefecto));
        var result = await provider.ConsultAsync(Ctx(), ct);

        handler.LastRequest.Should().BeNull();
        result.Overall.Should().Be("green");
        result.Provider.Should().Be("kyverum_fines");
    }

    [Fact]
    public async Task ConsultAsync_ModoMock_EtiquetaLosChecksConSuPropioProveedor()
    {
        // El mapper compartido etiqueta con flit_fines; este proveedor debe reescribir el Source
        // para que la trazabilidad apunte a quien respondió realmente.
        var ct = TestContext.Current.CancellationToken;

        var result = await BuildProvider().ConsultAsync(Ctx(), ct);

        result.Checks.Should().AllSatisfy(c => c.Source.Should().Be("kyverum_fines"));
    }

    [Fact]
    public async Task ConsultAsync_ModoMock_UsaLasClavesDelContratoCompartido()
    {
        // Los tres proveedores de comparendos deben emitir las mismas claves: de ellas cuelga el
        // gate de radicación al OT.
        var ct = TestContext.Current.CancellationToken;

        var result = await BuildProvider().ConsultAsync(Ctx(), ct);

        result.Checks.Select(c => c.Key).Should().BeEquivalentTo([
            FinesCheckFactory.KeyMultas,
            FinesCheckFactory.KeyAcuerdos,
        ]);
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_ConApiKey_EnviaLaCabeceraConElEsquema()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("""{"multas":[],"acuerdosPago":[]}""");

        await BuildProvider("real", handler, apiKey: "kf_test_secreto").ConsultAsync(Ctx(), ct);

        var auth = handler.LastRequest!.Headers.Authorization;
        auth!.Scheme.Should().Be("Bearer");
        auth.Parameter.Should().Be("kf_test_secreto");
        handler.LastRequest.RequestUri!.Query.Should().Contain("documentNumber=900123456");
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_SinApiKey_NoEnviaLaCabecera()
    {
        // Evita mandar "Bearer " vacío, que el proveedor rechazaría con un 401 confuso.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("""{"multas":[],"acuerdosPago":[]}""");

        await BuildProvider("real", handler, apiKey: "").ConsultAsync(Ctx(), ct);

        handler.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_ConMultasPendientes_DevuelveWarnNoFail()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler(
            """{"multas":[{"estadoComparendo":"Pendiente","valorPagar":"500000"}],"acuerdosPago":[]}""");

        var result = await BuildProvider("real", handler, apiKey: "k").ConsultAsync(Ctx(), ct);

        result.Checks.Single(c => c.Key == FinesCheckFactory.KeyMultas).Status.Should().Be("warn");
        result.Overall.Should().Be("yellow");
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_401_DevuelveErrorSinLanzarYSinFiltrarElProveedor()
    {
        // Riesgo documentado: en real sin credencial válida esto es un bloqueo duro. Debe al menos
        // degradar con un mensaje de usuario que no exponga el proveedor ni el detalle técnico.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("", HttpStatusCode.Unauthorized);

        var result = await BuildProvider("real", handler, apiKey: "invalida").ConsultAsync(Ctx(), ct);

        result.Overall.Should().Be("red");
        result.Checks.Should().ContainSingle(c => c.Status == "error");
        result.Checks.Single().Message.Should().NotContainAny("Kyverum", "KYVERUM", "401");
    }

    [Fact]
    public async Task ConsultAsync_SinDocumento_DevuelveUnknownSinLlamarAlApi()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("{}");

        var result = await BuildProvider("real", handler, apiKey: "k").ConsultAsync(Ctx(null, null), ct);

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
