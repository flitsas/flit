using System.Net;
using System.Text;
using Flit.Admin.Domain.Improntas;
using Flit.Infrastructure.Improntas;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Improntas;

/// <summary>
/// Uso de ejemplo:
/// var client = new ImprontaRuntClient(httpClient, Options.Create(new ImprontaRuntOptions { ApiKey = "kr_live_..." }), logger);
/// var result = await client.GenerarAsync(new ImprontaExternalRequest("ABC123", "MOT1", null, null, "TOYOTA", "COROLLA", "2022", "CERVIAL", "900123456-1", "MANIZALES", "jefe.instructores"), ct);
/// HU #10465 — AC1 (200 ⇒ DTO parseado), AC2 (VALIDATION_ERROR ⇒ excepción no transitoria) y
/// AC3 (401 ambiguo inspeccionando error.message ⇒ UNAUTHORIZED no transitorio; 502 ⇒ UPSTREAM_UNAVAILABLE transitorio).
/// </summary>
public sealed class ImprontaRuntClientTests
{
    private static readonly ImprontaExternalRequest ValidRequest = new(
        Placa: "ABC123",
        NumMotor: "MOT12345",
        NumChasis: "CHA98765",
        NumSerie: "5HTSN3527D7T91789",
        Marca: "TOYOTA",
        Linea: "COROLLA",
        Modelo: "2022",
        OrgNombre: "CERVIAL",
        OrgNit: "900123456-1",
        OrgCiudad: "MANIZALES",
        Operador: "jefe.instructores");

    private static ImprontaRuntClient Client(MockHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://runt.kyverum.test") },
            Options.Create(new ImprontaRuntOptions { BaseUrl = "https://runt.kyverum.test", ApiKey = "kr_live_secret" }),
            NullLogger<ImprontaRuntClient>.Instance);

    // ── AC1 — Escenario positivo ────────────────────────────────────────────────────────
    [Fact]
    public async Task AC1_SolicitudValida_Devuelve200ConDtoParseado()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """
            {"ok":true,"pdf":"data:application/pdf;base64,JVBERi0xLjQK","hash":"9f2cabcdef0123456789","radicado":"IMPR-LX3F8K2A","fechaImpresa":"2026-07-02T14:19:41"}
            """));

        var result = await Client(handler).GenerarAsync(ValidRequest, ct);

        result.PdfDataUri.Should().Be("data:application/pdf;base64,JVBERi0xLjQK");
        result.Hash.Should().Be("9f2cabcdef0123456789");
        result.Radicado.Should().Be("IMPR-LX3F8K2A");
        result.FechaImpresa.Should().Be("2026-07-02T14:19:41");
        // La API key viaja en el header Authorization, con el scheme configurado.
        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("kr_live_secret");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/improntas:generar");
        // El body enviado corresponde al contrato (placa + al menos un identificador + org/operador).
        handler.LastBody.Should().Contain("\"placa\":\"ABC123\"");
        handler.LastBody.Should().Contain("\"operador\":\"jefe.instructores\"");
    }

    [Fact]
    public async Task AC1_SolicitudValida_NoLanzaExcepcion()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """
            {"ok":true,"pdf":"data:application/pdf;base64,JVBERi0xLjQK","hash":"9f2c","radicado":"IMPR-1","fechaImpresa":"2026-07-02T14:19:41"}
            """));

        var act = async () => await Client(handler).GenerarAsync(ValidRequest, ct);

        await act.Should().NotThrowAsync();
    }

    // ── AC2 — Escenario negativo ────────────────────────────────────────────────────────
    [Fact]
    public async Task AC2_DatosVehiculoIncompletos_LanzaExcepcionNoTransitoriaConDetalleValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.UnprocessableEntity, """
            {"error":{"code":"VALIDATION_ERROR","message":"Cuerpo inválido","traceId":"trace-1","details":[{"field":"numMotor","reason":"Se requiere al menos uno: numMotor, numChasis o numSerie"}]}}
            """));

        var act = async () => await Client(handler).GenerarAsync(ValidRequest, ct);

        var ex = await act.Should().ThrowAsync<ImprontaRuntException>();
        ex.Which.IsTransient.Should().BeFalse();
        ex.Which.Message.Should().Contain("numMotor");
        ex.Which.Message.Should().Contain("Se requiere al menos uno");
        // La API key nunca viaja en el mensaje de la excepción.
        ex.Which.Message.Should().NotContain("kr_live_secret");
    }

    [Fact]
    public async Task AC2_ErrorValidacion_NoIncluyeApiKeyEnElMensaje()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.UnprocessableEntity, """
            {"error":{"code":"VALIDATION_ERROR","message":"Cuerpo inválido","details":[{"field":"placa","reason":"obligatorio"}]}}
            """));

        var act = async () => await Client(handler).GenerarAsync(ValidRequest, ct);

        (await act.Should().ThrowAsync<ImprontaRuntException>()).Which.Message.Should().NotContain("Bearer");
    }

    // ── AC3 — Escenario de borde ────────────────────────────────────────────────────────
    [Fact]
    public async Task AC3_ApiKeyInvalidaOScopeInsuficiente_Mapea401AUnauthorizedNoTransitorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.Unauthorized, """
            {"error":{"code":"UNAUTHORIZED","message":"Scope insuficiente para esta operación.","traceId":"trace-2"}}
            """));

        var act = async () => await Client(handler).GenerarAsync(ValidRequest, ct);

        var ex = await act.Should().ThrowAsync<ImprontaRuntException>();
        ex.Which.IsTransient.Should().BeFalse();
        // El cliente inspecciona error.message del cuerpo (no solo el status 401 ambiguo).
        ex.Which.Message.Should().Contain("Scope insuficiente para esta operación.");
        ex.Which.Message.Should().NotContain("kr_live_secret");
    }

    [Fact]
    public async Task AC3_KeyInvalidaConMensajeDistinto_TambienMapeaAUnauthorizedNoTransitorio()
    {
        var ct = TestContext.Current.CancellationToken;
        // Mismo status 401, mensaje distinto (key inválida en vez de scope insuficiente): Kyverum no
        // los distingue por status code, así que ambos casos deben mapear igual (no transitorio).
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.Unauthorized, """
            {"error":{"code":"UNAUTHORIZED","message":"API key inválida o revocada."}}
            """));

        var act = async () => await Client(handler).GenerarAsync(ValidRequest, ct);

        var ex = await act.Should().ThrowAsync<ImprontaRuntException>();
        ex.Which.IsTransient.Should().BeFalse();
        ex.Which.Message.Should().Contain("API key inválida o revocada.");
    }

    [Fact]
    public async Task AC3_UpstreamUnavailable502_MapeaATransitorioReintentable()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.BadGateway, """
            {"error":{"code":"UPSTREAM_UNAVAILABLE","message":"Servicio RUNT saturado, reintente."}}
            """));

        var act = async () => await Client(handler).GenerarAsync(ValidRequest, ct);

        var ex = await act.Should().ThrowAsync<ImprontaRuntException>();
        ex.Which.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_Timeout_MapeaATransitorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => throw new TaskCanceledException("timeout"));

        var act = async () => await Client(handler).GenerarAsync(ValidRequest, ct);

        (await act.Should().ThrowAsync<ImprontaRuntException>()).Which.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_RespuestaOkSinCamposObligatorios_LanzaExcepcionNoTransitoria()
    {
        var ct = TestContext.Current.CancellationToken;
        // 200 OK pero sin hash/radicado ⇒ respuesta inválida (definitiva, no reintentable).
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """{"ok":true,"pdf":"data:application/pdf;base64,AAAA"}"""));

        var act = async () => await Client(handler).GenerarAsync(ValidRequest, ct);

        (await act.Should().ThrowAsync<ImprontaRuntException>()).Which.IsTransient.Should().BeFalse();
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
