using System.Net;
using System.Text;
using Flit.Infrastructure.Improntas;
using Flit.Infrastructure.KyverumRunt;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.KyverumRunt;

/// <summary>
/// HU #10478 — cliente de consultas Kyverum RUNT (<c>vehiculos:consultar</c> / <c>personas:consultar</c>).
/// Cubre: request bien formado (endpoint, auth header, cuerpo), <c>ok:false</c> ⇒ excepción "no
/// encontrado", y clasificación de errores (401 no transitorio inspeccionando <c>error.message</c>,
/// 502/timeout transitorios) sin filtrar la API key. Reutiliza <see cref="ImprontaRuntOptions"/>
/// (misma config <c>KYVERUM_RUNT_*</c>), sin tocar el flujo de improntas.
/// </summary>
public sealed class KyverumRuntApiClientTests
{
    private static KyverumRuntApiClient Client(MockHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://runt.kyverum.test") },
            Options.Create(new ImprontaRuntOptions { BaseUrl = "https://runt.kyverum.test", ApiKey = "kr_live_secret" }),
            NullLogger<KyverumRuntApiClient>.Instance);

    // ── Vehículo por VIN ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ConsultarVehiculoPorVin_Ok_DevuelveDtoParseadoYEnviaBodyConVin()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """
            {"ok":true,"data":{"vehiculo":{"placa":"QYQ132","vin":"LRWYGCFJ5TC576828","marca":"TESLA","estadoAutomotor":"ACTIVO","gravamenes":"NO","prendas":"SI"},"soat":[{"estado":"VIGENTE","fechaVencimSoat":"2027-06-12T00:00:00.000-05:00","razonSocialAsegur":"LA PREVISORA S.A.COMPAÑIA DE SEGUROS"}],"rtm":[]},"fromCache":false}
            """));

        var result = await Client(handler).ConsultarVehiculoAsync(
            new KyverumRuntVehicleQuery(Vin: "LRWYGCFJ5TC576828", Placa: null, Documento: null, TipoDocumento: null), ct);

        result.Ok.Should().BeTrue();
        result.Data!.Vehiculo!.Placa.Should().Be("QYQ132");
        result.Data.Vehiculo.Marca.Should().Be("TESLA");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/vehiculos:consultar");
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be("kr_live_secret");
        handler.LastBody.Should().Contain("\"vin\":\"LRWYGCFJ5TC576828\"");
        // Por VIN no se envía placa/documento.
        handler.LastBody.Should().NotContain("placa");
        handler.LastBody.Should().NotContain("documento");
    }

    // ── Vehículo por placa ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ConsultarVehiculoPorPlaca_EnviaPlacaDocumentoYTipoDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """
            {"ok":true,"data":{"vehiculo":{"placa":"JNH38H","marca":"YAMAHA"}},"fromCache":false}
            """));

        var result = await Client(handler).ConsultarVehiculoAsync(
            new KyverumRuntVehicleQuery(Vin: null, Placa: "JNH38H", Documento: "1193552679", TipoDocumento: "C"), ct);

        result.Ok.Should().BeTrue();
        handler.LastBody.Should().Contain("\"placa\":\"JNH38H\"");
        handler.LastBody.Should().Contain("\"documento\":\"1193552679\"");
        handler.LastBody.Should().Contain("\"tipoDocumento\":\"C\"");
        handler.LastBody.Should().NotContain("\"vin\"");
    }

    [Fact]
    public async Task ConsultarVehiculoPorPlaca_SinTipoDocumento_OmiteElCampo()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """{"ok":true,"data":{"vehiculo":{"placa":"JNH38H"}}}"""));

        await Client(handler).ConsultarVehiculoAsync(
            new KyverumRuntVehicleQuery(Vin: null, Placa: "JNH38H", Documento: "1193552679", TipoDocumento: null), ct);

        handler.LastBody.Should().Contain("\"placa\":\"JNH38H\"");
        handler.LastBody.Should().NotContain("tipoDocumento");
    }

    [Fact]
    public async Task ConsultarVehiculo_OkFalse_LanzaNotFoundNoTransitorioConMensaje()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """
            {"ok":false,"message":"No se pudo obtener datos del vehículo.","fromCache":false}
            """));

        var act = async () => await Client(handler).ConsultarVehiculoAsync(
            new KyverumRuntVehicleQuery(Vin: "LRWYGCFJ5TC576828", Placa: null, Documento: null, TipoDocumento: null), ct);

        var ex = await act.Should().ThrowAsync<KyverumRuntException>();
        ex.Which.IsTransient.Should().BeFalse();
        ex.Which.IsNotFound.Should().BeTrue();
        ex.Which.Message.Should().Contain("No se pudo obtener datos del vehículo.");
    }

    // ── Persona / conductor ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ConsultarPersona_Ok_DevuelveDtoYEnviaDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """
            {"ok":true,"persona":{"nombres":"DANIEL ","apellidos":"AMADO GARCIA","estadoPersona":"ACTIVA","estadoConductor":"ACTIVO"},"licencias":[{"estadoLicencia":"ACTIVA","detalleLicencia":[{"categoria":"A2"}]}],"multas":{"tieneMultas":"SI"},"fromCache":false}
            """));

        var result = await Client(handler).ConsultarPersonaAsync(new KyverumRuntPersonaQuery("1193552679", "C"), ct);

        result.Ok.Should().BeTrue();
        result.Persona!.Apellidos.Should().Be("AMADO GARCIA");
        result.Multas!.TieneMultas.Should().Be("SI");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/personas:consultar");
        handler.LastBody.Should().Contain("\"documento\":\"1193552679\"");
        handler.LastBody.Should().Contain("\"tipoDocumento\":\"C\"");
    }

    [Fact]
    public async Task ConsultarPersona_SinTipoDocumento_OmiteElCampo()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """{"ok":true,"persona":{"nombres":"DANIEL","apellidos":"AMADO"}}"""));

        await Client(handler).ConsultarPersonaAsync(new KyverumRuntPersonaQuery("1193552679", null), ct);

        handler.LastBody.Should().Contain("\"documento\":\"1193552679\"");
        handler.LastBody.Should().NotContain("tipoDocumento");
    }

    [Fact]
    public async Task ConsultarPersona_OkFalse_LanzaNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """
            {"ok":false,"message":"Persona no encontrada en el RUNT","fromCache":false}
            """));

        var act = async () => await Client(handler).ConsultarPersonaAsync(new KyverumRuntPersonaQuery("0000", null), ct);

        var ex = await act.Should().ThrowAsync<KyverumRuntException>();
        ex.Which.IsNotFound.Should().BeTrue();
        ex.Which.IsTransient.Should().BeFalse();
        ex.Which.Message.Should().Contain("Persona no encontrada");
    }

    // ── Clasificación de errores HTTP ───────────────────────────────────────────────────────
    [Fact]
    public async Task Error401_UnauthorizedNoTransitorio_InspeccionaMensajeYNoFiltraApiKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.Unauthorized, """
            {"error":{"code":"UNAUTHORIZED","message":"Scope insuficiente para esta operación.","traceId":"trace-1"}}
            """));

        var act = async () => await Client(handler).ConsultarPersonaAsync(new KyverumRuntPersonaQuery("1193552679", "C"), ct);

        var ex = await act.Should().ThrowAsync<KyverumRuntException>();
        ex.Which.IsTransient.Should().BeFalse();
        ex.Which.IsNotFound.Should().BeFalse();
        ex.Which.Message.Should().Contain("Scope insuficiente para esta operación.");
        ex.Which.Message.Should().NotContain("kr_live_secret");
    }

    [Fact]
    public async Task Error422_ValidationError_NoTransitorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.UnprocessableEntity, """
            {"error":{"code":"VALIDATION_ERROR","message":"Cuerpo inválido"}}
            """));

        var act = async () => await Client(handler).ConsultarVehiculoAsync(
            new KyverumRuntVehicleQuery(Vin: null, Placa: "JNH38H", Documento: "1193552679", TipoDocumento: null), ct);

        var ex = await act.Should().ThrowAsync<KyverumRuntException>();
        ex.Which.IsTransient.Should().BeFalse();
        ex.Which.Message.Should().Contain("Cuerpo inválido");
        ex.Which.Message.Should().NotContain("kr_live_secret");
    }

    [Fact]
    public async Task Error502_UpstreamUnavailable_Transitorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.BadGateway, """
            {"error":{"code":"UPSTREAM_UNAVAILABLE","message":"Servicio RUNT saturado, reintente."}}
            """));

        var act = async () => await Client(handler).ConsultarVehiculoAsync(
            new KyverumRuntVehicleQuery(Vin: "LRWYGCFJ5TC576828", Placa: null, Documento: null, TipoDocumento: null), ct);

        (await act.Should().ThrowAsync<KyverumRuntException>()).Which.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task Timeout_Transitorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => throw new TaskCanceledException("timeout"));

        var act = async () => await Client(handler).ConsultarPersonaAsync(new KyverumRuntPersonaQuery("1193552679", "C"), ct);

        (await act.Should().ThrowAsync<KyverumRuntException>()).Which.IsTransient.Should().BeTrue();
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <summary>Body capturado al enviar: el request se dispone tras la llamada, así que se lee aquí.</summary>
        public string? LastBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(responder(request, cancellationToken));
        }
    }
}
