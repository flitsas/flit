using System.Net;
using System.Text;
using Flit.Infrastructure.Kyverum;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Kyverum;

public sealed class KyverumVerifyClientTests
{
    private static readonly KyverumVerifyStartRequest Request =
        new(Guid.NewGuid(), Guid.NewGuid(), "comprador", "Juan Perez", "CC", "123456", "juan@example.com");

    private static KyverumVerifyClient Client(MockHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://verify.kyverum.test") },
            Options.Create(new KyverumOptions { BaseUrl = "https://verify.kyverum.test", ApiKey = "secret-key" }),
            NullLogger<KyverumVerifyClient>.Instance);

    [Fact]
    public async Task Start_Success_ReturnsCaptureUrlSecretAndSanitizedPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.Created,
            """{"id":"kyv_123","status":"pending","webhookSecret":"whsec_resp","captureLinks":[{"captureUrl":"https://verify.kyverum.test/session/s1?t=abc"}]}"""));

        var result = await Client(handler).StartVerificationAsync(Request, ct);

        result.VerificationId.Should().Be("kyv_123");
        result.CaptureUrl.Should().Be("https://verify.kyverum.test/session/s1?t=abc");
        // El secreto del webhook lo devuelve Kyverum en el create.
        result.WebhookSecret.Should().Be("whsec_resp");
        result.ProviderStatus.Should().Be("pending");
        // Payload sanitizado: trazabilidad sin secreto ni PII cruda.
        result.RawPayloadSanitized.Should().Contain("kyv_123");
        result.RawPayloadSanitized.Should().NotContain("whsec_resp");
        result.RawPayloadSanitized.Should().NotContain("Juan Perez");
        // La API key viajó en el header Authorization y se envió Idempotency-Key.
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("secret-key");
        handler.LastRequest!.Headers.Contains("Idempotency-Key").Should().BeTrue();
        // POST al endpoint correcto del contrato.
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/validations");
        // El correo del sujeto viaja en el body (subjects[].email) para que Kyverum notifique al usuario.
        handler.LastBody.Should().Contain("\"email\"");
        handler.LastBody.Should().Contain("juan@example.com");
    }

    [Fact]
    public async Task Start_4xx_ThrowsDefinitiveError()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.BadRequest, """{"error":"bad"}"""));

        var act = async () => await Client(handler).StartVerificationAsync(Request, ct);

        var ex = await act.Should().ThrowAsync<KyverumVerifyException>();
        ex.Which.Transient.Should().BeFalse();
        ex.Which.Message.Should().NotContain("secret-key");
    }

    [Fact]
    public async Task Start_5xx_ThrowsTransientError()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var act = async () => await Client(handler).StartVerificationAsync(Request, ct);

        (await act.Should().ThrowAsync<KyverumVerifyException>()).Which.Transient.Should().BeTrue();
    }

    [Fact]
    public async Task Start_Timeout_ThrowsTransientError()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => throw new TaskCanceledException("timeout"));

        var act = async () => await Client(handler).StartVerificationAsync(Request, ct);

        (await act.Should().ThrowAsync<KyverumVerifyException>()).Which.Transient.Should().BeTrue();
    }

    [Fact]
    public async Task Start_MalformedBody_ThrowsDefinitiveError()
    {
        var ct = TestContext.Current.CancellationToken;
        // 200 OK pero sin verificationId ⇒ respuesta inválida (definitiva).
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """{"status":"pending"}"""));

        var act = async () => await Client(handler).StartVerificationAsync(Request, ct);

        (await act.Should().ThrowAsync<KyverumVerifyException>()).Which.Transient.Should().BeFalse();
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
