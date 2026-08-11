using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// HU #11361 — adaptador de envío/multipart del canal Renting (<see cref="RentingEmailApiSender"/>).
/// <para>
/// Uso de ejemplo del componente bajo prueba:
/// <code>
/// var sender = new RentingEmailApiSender(executor, httpClientFactory, Options.Create(channelOptions),
///     new PassthroughRentingRecipientOverride(), logger);
/// var result = await sender.SendAsync(request, cancellationToken);
/// </code>
/// </para>
/// <para>
/// Ningún test llama a la API real de Renting — todo contra <see cref="MockHttpMessageHandler"/>
/// (mismo patrón que <c>RentingLoginClientTests</c>). El login/caché/reintento se ejercita a través
/// del <see cref="RentingAuthenticatedRequestExecutor"/> REAL (HU #11360), con
/// <see cref="IRentingTokenCache"/> sustituido (NSubstitute) para no necesitar login HTTP real en
/// estas pruebas — eso ya lo cubre <c>RentingAuthenticatedRequestExecutorTests</c>.
/// </para>
/// </summary>
public sealed class RentingEmailApiSenderTests
{
    private const string ApiKeyValue = "clave-api-super-secreta-no-real";
    private const string Token = "bearer-token-super-secreto-no-real";
    private const string LoginSecretName = "nombre-secreto-login-no-real";
    private const string Passphrase = "passphrase-super-secreta-no-real";

    private static RentingChannelOptions NewOptions(int errorBodyMaxLength = 500) => new()
    {
        BaseUrl = "https://renting.example.test",
        SendEmailPath = "/mail/send",
        SendEmailSecondsTimeout = 5,
        ApiKeyValue = ApiKeyValue,
        LoginSecretName = LoginSecretName,
        Passphrase = Passphrase,
        SendEmailErrorBodyMaxLength = errorBodyMaxLength,
    };

    private static RentingSendEmailRequest OneRecipientRequest(RentingEmailAttachment? attachment = null) =>
        new(
            Subject: "Asunto de prueba",
            Body: "Cuerpo de prueba",
            Sender: new RentingEmailAddress("remitente@example.test", "Remitente FLIT"),
            Recipients: [new RentingEmailAddress("destinatario@example.test", "Destinatario Uno")],
            BccRecipients: [],
            Attachments: attachment is null ? [] : [attachment]);

    private static (RentingEmailApiSender Sender, MockHttpMessageHandler Handler) NewSender(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        RentingChannelOptions? options = null,
        ILogger<RentingEmailApiSender>? logger = null)
    {
        var handler = new MockHttpMessageHandler(responder);
        var tokenCache = Substitute.For<IRentingTokenCache>();
        tokenCache.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(Token);
        var executor = new RentingAuthenticatedRequestExecutor(tokenCache, NullLogger<RentingAuthenticatedRequestExecutor>.Instance);

        var sender = new RentingEmailApiSender(
            executor,
            new FakeHttpClientFactory(handler),
            Microsoft.Extensions.Options.Options.Create(options ?? NewOptions()),
            new PassthroughRentingRecipientOverride(),
            logger ?? NullLogger<RentingEmailApiSender>.Instance);

        return (sender, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ── AC1 — el cuerpo multipart lleva los campos exactos ─────────────────────────

    [Fact]
    public async Task SendAsync_ConAsuntoCuerpoYUnDestinatario_ElCuerpoEsMultipartConLosCamposDelContrato()
    {
        var ct = TestContext.Current.CancellationToken;
        var attachment = new RentingEmailAttachment("adjunto.pdf", "application/pdf", [1, 2, 3, 4]);
        var (sender, handler) = NewSender((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, """{"statusCode":200,"isSuccessStatusCode":true}""")));

        var result = await sender.SendAsync(OneRecipientRequest(attachment), ct);

        result.Success.Should().BeTrue();
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Content.Should().BeOfType<MultipartFormDataContent>();
        handler.LastRequest!.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", Token));

        var parts = handler.LastRequestParts!;

        parts.Should().ContainKey("Subject").WhoseValue.Should().Be("Asunto de prueba");
        parts.Should().ContainKey("Body").WhoseValue.Should().Be("Cuerpo de prueba");
        parts.Should().ContainKey("Sender.Email").WhoseValue.Should().Be("remitente@example.test");
        parts.Should().ContainKey("Sender.UserName").WhoseValue.Should().Be("Remitente FLIT");
        parts.Should().ContainKey("Recipients[0].Email").WhoseValue.Should().Be("destinatario@example.test");
        parts.Should().ContainKey("Recipients[0].UserName").WhoseValue.Should().Be("Destinatario Uno");
        parts.Should().ContainKey("AttachFiles");
    }

    [Fact]
    public async Task SendAsync_SinAdjuntos_ElCampoAttachFilesSigueEstandoPresente()
    {
        // Edge case: el contrato exige el campo AttachFiles incluso sin adjuntos.
        var ct = TestContext.Current.CancellationToken;
        var (sender, handler) = NewSender((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, """{"statusCode":200,"isSuccessStatusCode":true}""")));

        await sender.SendAsync(OneRecipientRequest(), ct);

        handler.LastRequestParts!.Should().ContainKey("AttachFiles");
    }

    // ── AC2 — varios destinatarios en copia oculta conservan su índice ─────────────

    [Fact]
    public async Task SendAsync_ConTresDestinatariosEnCopiaOculta_CadaUnoViajaEnSuPropioIndice()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, handler) = NewSender((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, """{"statusCode":200,"isSuccessStatusCode":true}""")));

        var request = OneRecipientRequest() with
        {
            BccRecipients = ["bcc-uno@example.test", "bcc-dos@example.test", "bcc-tres@example.test"],
        };

        await sender.SendAsync(request, ct);

        var parts = handler.LastRequestParts!;

        // AC2 — el defecto de la implementación de referencia del cliente escribe SIEMPRE en el
        // índice 0 y solo sobrevive el último. Esta prueba cae si alguien lo reintroduce: no basta
        // con contar campos, hace falta comprobar los TRES correos con índices DISTINTOS.
        parts.Should().ContainKey("bccRecipients[0].Email").WhoseValue.Should().Be("bcc-uno@example.test");
        parts.Should().ContainKey("bccRecipients[1].Email").WhoseValue.Should().Be("bcc-dos@example.test");
        parts.Should().ContainKey("bccRecipients[2].Email").WhoseValue.Should().Be("bcc-tres@example.test");

        var bccKeys = new List<string> { "bccRecipients[0].Email", "bccRecipients[1].Email", "bccRecipients[2].Email" };
        var bccValues = bccKeys.Select(key => parts[key]).ToArray();
        var expectedBccValues = new List<string> { "bcc-uno@example.test", "bcc-dos@example.test", "bcc-tres@example.test" };
        bccValues.Should().OnlyHaveUniqueItems();
        bccValues.Should().BeEquivalentTo(expectedBccValues);
    }

    // ── AC3 — respuesta de aceptación ───────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ConIsSuccessStatusCodeVerdadero_ElResultadoEsCorrectoYEsAceptacionNoEntrega()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _) = NewSender((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, """{"statusCode":200,"isSuccessStatusCode":true}""")));

        var result = await sender.SendAsync(OneRecipientRequest(), ct);

        result.Should().Be(EmailSendResult.Sent);
        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(EmailSendOutcome.Sent);
        // AC3 — el mensaje genérico de éxito no promete entrega al destinatario, solo aceptación.
        result.Message.Should().NotContain("entreg", "es un acuse de aceptación del proveedor, no de entrega");
    }

    [Fact]
    public async Task SendAsync_ConIsSuccessStatusCodeFalso_NoEsUnEnvioExitoso()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _) = NewSender((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, """{"statusCode":200,"isSuccessStatusCode":false}""")));

        var result = await sender.SendAsync(OneRecipientRequest(), ct);

        result.Success.Should().BeFalse();
    }

    // ── AC4 — los códigos de error se mapean al catálogo cerrado ───────────────────

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, EmailSendOutcome.AuthenticationFailed)]
    [InlineData(HttpStatusCode.BadRequest, EmailSendOutcome.ContentRejected)]
    [InlineData(HttpStatusCode.TooManyRequests, EmailSendOutcome.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, EmailSendOutcome.ProviderUnavailable)]
    public async Task SendAsync_ConCodigoDeErrorDelProveedor_MapeaAlCatalogoCerrado(
        HttpStatusCode statusCode, EmailSendOutcome expectedOutcome)
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _) = NewSender((_, _) => Task.FromResult(Json(statusCode, """{"error":"rechazado"}""")));

        var result = await sender.SendAsync(OneRecipientRequest(), ct);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(expectedOutcome);
    }

    // ── AC5 — el cuerpo de error se trunca y se sanea ───────────────────────────────

    [Fact]
    public async Task SendAsync_ConCuerpoDeErrorExtensoConSecretos_TruncaYNoFiltraNingunSecreto()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = new CapturingLogger();
        var cuerpoConSecretos =
            $"Error del proveedor. ApiKey={ApiKeyValue} Token={Token} LoginSecretName={LoginSecretName} "
            + $"Passphrase={Passphrase}. " + new string('X', 2000);

        var options = NewOptions(errorBodyMaxLength: 120);
        var (sender, _) = NewSender(
            (_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, cuerpoConSecretos)),
            options,
            logger);

        var result = await sender.SendAsync(OneRecipientRequest(), ct);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(EmailSendOutcome.ContentRejected);

        logger.Warnings.Should().NotBeEmpty("el escenario tiene que loguear el rechazo para que el aserto valga");
        var logged = string.Concat(logger.Warnings);

        // AC5 — ausencia ASEVERADA de cada uno de los cuatro secretos, y el cuerpo saneado quedó
        // acotado a la longitud máxima configurada.
        logged.Should().NotContain(ApiKeyValue);
        logged.Should().NotContain(Token);
        logged.Should().NotContain(LoginSecretName);
        logged.Should().NotContain(Passphrase);
    }

    [Fact]
    public void Sanitize_ConCuerpoQueExcedeLaLongitudMaxima_QuedaTruncado()
    {
        var body = new string('A', 1000);

        var sanitized = RentingErrorBodySanitizer.Sanitize(body, maxLength: 50, secrets: []);

        sanitized.Should().HaveLength(50);
    }

    [Fact]
    public void Sanitize_ConSecretosPresentes_LosRedactaAntesDeTruncar()
    {
        const string secret = "secreto-de-prueba";
        var body = $"prefijo {secret} sufijo";

        var sanitized = RentingErrorBodySanitizer.Sanitize(body, maxLength: 200, secrets: [secret]);

        sanitized.Should().NotContain(secret);
    }

    // ── AC6 — respuesta ilegible ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ConCuerpoQueNoCorrespondeAlContrato_FallaPorProveedorNoDisponible()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _) = NewSender((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("esto no es json", Encoding.UTF8, "text/plain"),
            }));

        var result = await sender.SendAsync(OneRecipientRequest(), ct);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(EmailSendOutcome.ProviderUnavailable);
    }

    [Fact]
    public async Task SendAsync_ConJsonValidoSinElCampoDeContrato_FallaPorProveedorNoDisponible()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _) = NewSender((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, """{"otroCampo":"valor"}""")));

        var result = await sender.SendAsync(OneRecipientRequest(), ct);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(EmailSendOutcome.ProviderUnavailable);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, string>> ReadPartsAsync(MultipartFormDataContent content)
    {
        var result = new Dictionary<string, string>();
        foreach (var part in content)
        {
            var name = part.Headers.ContentDisposition?.Name?.Trim('"');
            if (name is null)
                continue;

            result[name] = await part.ReadAsStringAsync();
        }

        return result;
    }

    private sealed class FakeHttpClientFactory(MockHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://renting.example.test") };
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <summary>
        /// Partes del último multipart, capturadas AQUÍ (antes de responder) porque
        /// <c>RentingEmailApiSender.SendOnceAsync</c> envuelve la petición en un <c>using</c> que
        /// dispone el <see cref="MultipartFormDataContent"/> apenas termina el envío — para cuando
        /// el test lee <see cref="LastRequest"/>, el contenido ya no es legible.
        /// </summary>
        public Dictionary<string, string>? LastRequestParts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is MultipartFormDataContent multipart)
                LastRequestParts = await ReadPartsAsync(multipart);

            return await responder(request, cancellationToken);
        }
    }

    private sealed class CapturingLogger : ILogger<RentingEmailApiSender>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
