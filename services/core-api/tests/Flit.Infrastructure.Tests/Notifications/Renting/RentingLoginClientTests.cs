using System.Net;
using System.Text;
using Flit.Infrastructure.Notifications.Renting;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// HU #11360 — implementación HTTP del login (<see cref="RentingLoginClient"/>). Contrato:
/// petición <c>{ secretName, subject }</c> a <c>RENTING_API_LOGIN_PATH</c>, respuesta
/// <c>{ token, refresh }</c>. Ningún test llama a la API real de Renting — todo contra
/// <see cref="MockHttpMessageHandler"/> (mismo patrón que <c>Kyverum.KyverumVerifyClientTests</c>).
/// <para>
/// Uso de ejemplo del componente bajo prueba:
/// <code>
/// var client = new RentingLoginClient(httpClientFactory, Options.Create(channelOptions));
/// var result = await client.LoginAsync(cancellationToken); // result.Token, result.Refresh
/// </code>
/// </para>
/// </summary>
public sealed class RentingLoginClientTests
{
    private static RentingChannelOptions NewOptions() => new()
    {
        BaseUrl = "https://renting.example.test",
        LoginPath = "/auth/login",
        LoginSecondsTimeout = 5,
        LoginSecretName = "secreto-renting-notificaciones",
        LoginSubject = "CN=renting-test",
    };

    private static RentingLoginClient NewClient(MockHttpMessageHandler handler, RentingChannelOptions? options = null) =>
        new(new FakeHttpClientFactory(handler), Options.Create(options ?? NewOptions()));

    [Fact]
    public async Task LoginAsync_ConRespuestaExitosa_EnviaSecretNameYSubjectYDevuelveElToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, """{"token":"jwt-de-prueba","refresh":"refresh-de-prueba"}""")));

        var result = await NewClient(handler).LoginAsync(ct);

        result.Token.Should().Be("jwt-de-prueba");
        result.Refresh.Should().Be("refresh-de-prueba");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/auth/login");
        handler.LastBody.Should().Contain("\"secretName\":\"secreto-renting-notificaciones\"");
        handler.LastBody.Should().Contain("\"subject\":\"CN=renting-test\"");
    }

    [Fact]
    public async Task LoginAsync_ConRespuestaDeErrorDelProveedor_LanzaRentingAuthenticationException()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")));

        var act = async () => await NewClient(handler).LoginAsync(ct);

        // AC6 — el fallo de login no se silencia: se propaga siempre como excepción tipada.
        await act.Should().ThrowAsync<RentingAuthenticationException>();
    }

    [Fact]
    public async Task LoginAsync_ConRespuestaSinToken_LanzaRentingAuthenticationException()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler(
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"refresh":"solo-refresh-sin-token"}""")));

        var act = async () => await NewClient(handler).LoginAsync(ct);

        // AC6 — nunca un token nulo/vacío tratado como éxito.
        await act.Should().ThrowAsync<RentingAuthenticationException>();
    }

    [Fact]
    public async Task LoginAsync_ConCuerpoQueNoEsJson_LanzaRentingAuthenticationException()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("no es json", Encoding.UTF8, "text/plain") }));

        var act = async () => await NewClient(handler).LoginAsync(ct);

        await act.Should().ThrowAsync<RentingAuthenticationException>();
    }

    [Fact]
    public async Task LoginAsync_ConElServidorColgado_AgotaElTimeoutPropioDeLoginYLanzaRentingAuthenticationException()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler(async (_, requestCt) =>
        {
            // Nunca responde dentro del LoginSecondsTimeout configurado (1s) — simula servidor colgado.
            await Task.Delay(TimeSpan.FromSeconds(10), requestCt);
            return Json(HttpStatusCode.OK, """{"token":"nunca-llega"}""");
        });

        var options = NewOptions();
        options.LoginSecondsTimeout = 1;

        var act = async () => await NewClient(handler, options).LoginAsync(ct);

        await act.Should().ThrowAsync<RentingAuthenticationException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHttpClientFactory(MockHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://renting.example.test") };
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return await responder(request, cancellationToken);
        }
    }
}
