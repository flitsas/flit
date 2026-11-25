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
        // Sin `expiresAt` en la respuesta → null (el handler cae al TTL local por defecto).
        result.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Start_Success_ParsesExpiresAt_FromResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        // Kyverum informa el vencimiento REAL del enlace de captura en `expiresAt`.
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.Created,
            """{"id":"kyv_123","status":"enviado","webhookSecret":"whsec_resp","expiresAt":"2026-07-03T17:00:37.019Z","captureLinks":[{"captureUrl":"https://verify.kyverum.test/session/s1?t=abc"}]}"""));

        var result = await Client(handler).StartVerificationAsync(Request, ct);

        result.ExpiresAt.Should().Be(new DateTimeOffset(2026, 7, 3, 17, 0, 37, 19, TimeSpan.Zero));
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

    [Fact]
    public async Task GetStatus_Approved_ReturnsStatusAndScore()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK,
            """{"id":"kyv_1","status":"aprobado","subjects":[{"rol":"comprador","status":"aprobado","score":88,"validadoAt":"2026-07-02T10:00:00Z","datosExtraidos":{"nombres":"ANDRES"}}]}"""));

        var result = await Client(handler).GetStatusAsync("kyv_1", "comprador", ct);

        result.Should().NotBeNull();
        result!.Status.Should().Be("aprobado");
        result.Score.Should().Be(88);
        // GET al endpoint de estado con el Bearer key.
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/validations/kyv_1");
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("secret-key");
        // Trazabilidad sanitizada: sin OCR/PII.
        result.RawPayloadSanitized.Should().Contain("aprobado").And.NotContain("ANDRES").And.NotContain("datosExtraidos");
    }

    [Fact]
    public async Task GetStatus_StillPending_ReturnsEnviado()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK,
            """{"id":"kyv_1","status":"enviado","subjects":[{"rol":"comprador","status":"enviado","score":null}]}"""));

        var result = await Client(handler).GetStatusAsync("kyv_1", "comprador", ct);

        result!.Status.Should().Be("enviado");
        result.Score.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_RechazadoTopLevel_WithoutClosedAt_ReturnsRechazadoIntento()
    {
        // Bug #11503: Kyverum reporta el status top-level "rechazado" tras CADA intento fallido dentro de
        // una misma validación (no solo al agotar los 3). Sin `result.closedAt` (señal de cierre real del
        // proveedor) NO es terminal: debe mapear a "rechazado_intento" para que el reconciliador decida con
        // el conteo de intentos, no congelar la validación en rechazado tras el primer intento.
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK,
            """{"id":"kyv_1","status":"rechazado","result":{"aprobado":false},"subjects":[{"rol":"comprador","status":"rechazado","score":40,"motivo":"El rostro no es completamente visible"}]}"""));

        var result = await Client(handler).GetStatusAsync("kyv_1", "comprador", ct);

        result.Should().NotBeNull();
        result!.Status.Should().Be("rechazado_intento");
        result.RawPayloadSanitized.Should().Contain("closed_at");
    }

    [Fact]
    public async Task GetStatus_RechazadoTopLevel_WithClosedAt_ReturnsRechazadoTerminal()
    {
        // Con `result.closedAt` presente, Kyverum SÍ cerró la validación (agotó reintentos): ahí el
        // "rechazado" top-level es terminal.
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK,
            """{"id":"kyv_1","status":"rechazado","result":{"aprobado":false,"closedAt":"2026-08-13T10:00:00Z"},"subjects":[{"rol":"comprador","status":"rechazado","score":30}]}"""));

        var result = await Client(handler).GetStatusAsync("kyv_1", "comprador", ct);

        result.Should().NotBeNull();
        result!.Status.Should().Be("rechazado");
        result.RawPayloadSanitized.Should().Contain("2026-08-13T10:00:00Z");
    }

    [Fact]
    public async Task GetStatus_NotFound_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await Client(handler).GetStatusAsync("kyv_x", null, ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_5xx_ThrowsTransient()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var act = async () => await Client(handler).GetStatusAsync("kyv_1", null, ct);

        (await act.Should().ThrowAsync<KyverumVerifyException>()).Which.Transient.Should().BeTrue();
    }

    // ── Reenvío (POST .../subjects/{subjectId}/reissue) ─────────────────────────

    private const string EstadoConSujeto =
        """{"status":"enviado","subjects":[{"id":"sub_1","rol":"titular","status":"enviado"}]}""";

    [Fact]
    public async Task Reissue_ObtieneSujetoYDevuelveEnlaceNuevo_SinTocarLaVerificacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((req, _) => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, EstadoConSujeto)
            : Json(HttpStatusCode.OK, """{"subjectId":"sub_1","captureUrl":"https://verify.kyverum.test/session/s2?t=new","enviado":true,"tieneEmail":true}"""));

        var result = await Client(handler).ReissueAsync("kyv_1", ct);

        result.Outcome.Should().Be(KyverumReissueOutcome.Reemitido);
        result.CaptureUrl.Should().Be("https://verify.kyverum.test/session/s2?t=new");
        result.Enviado.Should().BeTrue("Kyverum confirmó que el correo salió o quedó en cola");
        // La última llamada es el POST de reissue sobre el sujeto de la validación existente (no un create).
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/validations/kyv_1/subjects/sub_1/reissue");
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("secret-key");
        // enviarCorreo: true ⇒ Kyverum manda el enlace al correo ya registrado.
        handler.LastBody.Should().Contain("\"enviarCorreo\":true");
    }

    [Fact]
    public async Task Reissue_SinCorreoEnviado_DevuelveEnlacePeroNoConfirmaElEnvio()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((req, _) => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, EstadoConSujeto)
            : Json(HttpStatusCode.OK, """{"subjectId":"sub_1","captureUrl":"https://verify.kyverum.test/session/s3?t=new","enviado":false,"tieneEmail":false}"""));

        var result = await Client(handler).ReissueAsync("kyv_1", ct);

        result.Outcome.Should().Be(KyverumReissueOutcome.Reemitido);
        result.CaptureUrl.Should().Be("https://verify.kyverum.test/session/s3?t=new");
        result.Enviado.Should().BeFalse();
    }

    [Fact]
    public async Task Reissue_SiYaEstaAprobada_NoReenvia()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK,
            """{"status":"aprobado","result":{"aprobado":true,"closedAt":"2026-11-24T10:00:00Z"},"subjects":[{"id":"sub_1","status":"aprobado"}]}"""));

        var result = await Client(handler).ReissueAsync("kyv_1", ct);

        result.Outcome.Should().Be(KyverumReissueOutcome.Aprobada);
        // Solo la consulta de estado: nunca se llega al POST.
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task Reissue_Conflicto409_EsAprobadaEnElPostSinLanzar()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((req, _) => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, EstadoConSujeto)
            : Json(HttpStatusCode.Conflict, "{}"));

        var result = await Client(handler).ReissueAsync("kyv_1", ct);

        result.Outcome.Should().Be(KyverumReissueOutcome.Aprobada);
    }

    [Fact]
    public async Task Reissue_ValidacionDesconocida404_EsNoExiste()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.NotFound, "{}"));

        var result = await Client(handler).ReissueAsync("kyv_gone", ct);

        result.Outcome.Should().Be(KyverumReissueOutcome.NoExiste);
        result.CaptureUrl.Should().BeNull();
    }

    [Fact]
    public async Task Reissue_5xxEnElPost_EsTransitorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((req, _) => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, EstadoConSujeto)
            : Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var act = async () => await Client(handler).ReissueAsync("kyv_1", ct);

        (await act.Should().ThrowAsync<KyverumVerifyException>()).Which.Transient.Should().BeTrue();
    }

    [Fact]
    public async Task Reissue_4xxEnElPost_EsDefinitivo()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((req, _) => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, EstadoConSujeto)
            : Json(HttpStatusCode.BadRequest, "{}"));

        var act = async () => await Client(handler).ReissueAsync("kyv_1", ct);

        (await act.Should().ThrowAsync<KyverumVerifyException>()).Which.Transient.Should().BeFalse();
    }

    [Fact]
    public async Task Reissue_SinSujetoEnLaRespuesta_EsDefinitivo()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, """{"status":"enviado","subjects":[]}"""));

        var act = async () => await Client(handler).ReissueAsync("kyv_1", ct);

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
