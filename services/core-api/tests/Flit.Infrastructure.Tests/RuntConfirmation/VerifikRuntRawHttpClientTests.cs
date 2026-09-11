using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.RuntConfirmation;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.RuntConfirmation;

/// <summary>
/// Cliente crudo de Verifik para Confirmación RUNT (Feature #12276). Lo que importa aquí es la
/// evidencia: un error HTTP del proveedor tiene que quedar con su cuerpo y su código en el intento,
/// porque un «HTTP 409» pelado no se puede diagnosticar sin repetir la consulta pagada.
/// </summary>
public sealed class VerifikRuntRawHttpClientTests
{
    private static readonly RuntRawQuery ByPlate = RuntRawQuery.ByPlate("JNH38H", new RuntDocument("CC", "1193552679"));

    [Fact]
    public async Task ErrorHttp_ConservaElCuerpoDelProveedor_YPoneCodigoYMensajeEnElMotivo()
    {
        var handler = new MockHttpMessageHandler(_ => Json(HttpStatusCode.Conflict,
            """{"code":"MissingParameter","message":"missing documentType. missing documentNumber. missing plate"}"""));

        var result = await Client(handler).ConsultAsync(ByPlate, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RuntRawOutcome.Error);
        result.Message.Should().Be("HTTP 409 — MissingParameter: missing documentType. missing documentNumber. missing plate");
        using var raw = JsonDocument.Parse(result.RawJson!);
        raw.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        raw.RootElement.GetProperty("statusCode").GetInt32().Should().Be(409);
        raw.RootElement.GetProperty("providerBody").GetProperty("code").GetString().Should().Be("MissingParameter");
        handler.Requests.Should().ContainSingle("un 4xx es definitivo: no se reintenta");
    }

    [Fact]
    public async Task ErrorHttp_SinCuerpoJson_DejaSoloElStatus()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>Bad gateway</html>", Encoding.UTF8, "text/html"),
        });

        var result = await Client(handler).ConsultAsync(ByPlate, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RuntRawOutcome.Error);
        result.Message.Should().Be("HTTP 502");
        result.RawJson.Should().BeNull();
        handler.Requests.Should().HaveCount(2, "un 5xx es transitorio: un reintento");
    }

    [Fact]
    public async Task PorPlaca_MandaPlacaTipoYNumeroDeDocumento()
    {
        var handler = new MockHttpMessageHandler(_ => Json(HttpStatusCode.OK, """{"data":{"informacionGeneral":{"noPlaca":"JNH38H"}}}"""));

        var result = await Client(handler).ConsultAsync(ByPlate, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RuntRawOutcome.Found);
        handler.Requests.Single().RequestUri!.PathAndQuery.Should()
            .Be("/v2/co/runt/vehicle-by-plate?plate=JNH38H&documentType=CC&documentNumber=1193552679");
    }

    private static VerifikRuntRawHttpClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.verifik.test") },
            Options.Create(new VerifikOptions { BaseUrl = "https://api.verifik.test", ApiToken = "secret" }));

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
