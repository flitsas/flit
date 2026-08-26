using System.Net;
using System.Text;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// HU #11372 (decisión del PO 2026-08-11) — la exención de <see cref="ControlledMailboxRecipient"/>
/// solo es alcanzable desde <see cref="IExplicitChannelEmailSender.SendAsync(NotificationChannel, EmailMessage, CancellationToken)"/>
/// (el banco de pruebas de notificaciones); el camino de producción
/// (<see cref="IEmailSender.SendAsync(EmailMessage, CancellationToken)"/>, resuelto por
/// <see cref="TenantChannelEmailRouter"/>) nunca la propaga, así que el desvío OBLIGATORIO de la
/// HU #11364 (<see cref="RentingRecipientOverride"/>) sigue aplicando siempre en ese camino.
/// </summary>
/// <remarks>
/// Uso de ejemplo del componente bajo prueba (cadena completa, sin llamar a la API real de Renting):
/// <code>
/// var router = new TenantChannelEmailRouter(flitTransport, settingsRepo, rentingSender, options, logger);
/// var result = await router.SendAsync(NotificationChannel.TenantApi, message, ct); // banco de pruebas
/// var result2 = await router.SendAsync(message, ct); // camino de producción (EmailMessage)
/// </code>
/// Ningún test llama a la API real de Renting — todo contra <see cref="MockHttpMessageHandler"/>
/// (mismo patrón que <c>RentingEmailApiSenderTests</c>), con el desvío (<see cref="RentingRecipientOverride"/>)
/// REAL para que la barrera se ejercite de punta a punta, no solo con dobles.
/// </remarks>
public sealed class ControlledMailboxRecipientExemptionTests
{
    private const string Token = "bearer-token-super-secreto-no-real";
    private const string OriginalRecipientEmail = "cliente-real@example.test";
    private const string TestMailboxEmail = "buzon-pruebas@flit.example.test";
    private const string DivertedRecipientEmail = "desvio-qa@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RentingChannelOptions NewOptions(bool overrideEnabled) => new()
    {
        Enabled = true,
        BaseUrl = "https://renting.example.test",
        SendEmailPath = "/mail/send",
        SendEmailSecondsTimeout = 5,
        SendEmailSenderEmail = "canal@renting.test",
        SendEmailSenderUsername = "Canal Renting",
        SendRealRecipientsEnabled = !overrideEnabled,
        SendEmailDevelopmentRecipientEmail = DivertedRecipientEmail,
        SendEmailDevelopmentRecipientUsername = "Desvío QA",
    };

    private static (TenantChannelEmailRouter Router, MockHttpMessageHandler Handler) NewRouterWithRealRentingAdapter(
        RentingChannelOptions options)
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, """{"statusCode":200,"isSuccessStatusCode":true}""")));

        var tokenCache = Substitute.For<IRentingTokenCache>();
        tokenCache.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(Token);
        var executor = new RentingAuthenticatedRequestExecutor(tokenCache, NullLogger<RentingAuthenticatedRequestExecutor>.Instance);

        var recipientOverride = new RentingRecipientOverride(
            Options.Create(options), NullLogger<RentingRecipientOverride>.Instance);

        var rentingSender = new RentingEmailApiSender(
            executor,
            new FakeHttpClientFactory(handler),
            Options.Create(options),
            recipientOverride,
            NullLogger<RentingEmailApiSender>.Instance);

        var settingsRepo = Substitute.For<ITenantSettingsRepository>();
        var tenantId = Guid.Parse("11111111-2222-4000-8000-000000000099");
        settingsRepo.GetAsync(tenantId, Ct).Returns(new TenantSettings
        {
            TenantId = tenantId,
            AllowInitialRegistration = true,
            AllowMiscNewVehicles = true,
            OnlyOwnVehicles = false,
            SignatureVaultEnabled = false,
            NotificationChannel = NotificationChannel.TenantApi,
            NotificationTarget = NotificationTarget.Radicador,
            PaymentMethods = [],
        });

        var flitTransport = Substitute.For<IEmailSender>();

        var router = new TenantChannelEmailRouter(
            flitTransport,
            new NotificationChannelResolver(settingsRepo),
            rentingSender,
            Options.Create(options),
            NullLogger<TenantChannelEmailRouter>.Instance);

        return (router, handler);
    }

    private static EmailMessage ProductionMessage(string toEmail) =>
        new(
            TenantId: Guid.Parse("11111111-2222-4000-8000-000000000099"),
            TemplateKey: "analytics.alert",
            ToEmail: toEmail,
            ToName: "Cliente Real",
            Subject: "Asunto",
            HtmlBody: "<p>Cuerpo</p>");

    private static EmailMessage TestBenchMessage(string toEmail) =>
        new(
            TenantId: null,
            TemplateKey: "analytics.alert",
            ToEmail: toEmail,
            ToName: "Banco de pruebas de notificaciones",
            Subject: "Asunto",
            HtmlBody: "<p>Cuerpo</p>");

    // ── Banco de pruebas + TENANT_API: el buzón de pruebas SÍ llega al proveedor ───────────────

    [Fact]
    public async Task BancoDePruebas_ConCanalExplicitoTenantApi_ElBuzonDePruebasLlegaAlProveedorSinDesviar()
    {
        var options = NewOptions(overrideEnabled: true); // desvío obligatorio activo, como en producción
        var (router, handler) = NewRouterWithRealRentingAdapter(options);

        var result = await router.SendAsync(NotificationChannel.TenantApi, TestBenchMessage(TestMailboxEmail), Ct);

        result.Success.Should().BeTrue();
        result.RecipientDiverted.Should().BeFalse(
            "el buzón de pruebas ya es un destinatario controlado: no hubo desvío real que reportar");

        handler.LastRequestParts.Should().NotBeNull();
        handler.LastRequestParts!.Should().ContainKey("Recipients[0].Email").WhoseValue.Should().Be(TestMailboxEmail);
        handler.LastRequestParts!.Should().NotContainValue(DivertedRecipientEmail);
    }

    // ── LA PRUEBA MÁS IMPORTANTE: el camino de producción sigue desviando ──────────────────────

    [Fact]
    public async Task CaminoDeProduccion_ConTenantApiYDesvioActivo_SigueDesviandoYElDestinatarioOriginalNuncaLlegaAlProveedor()
    {
        var options = NewOptions(overrideEnabled: true);
        var (router, handler) = NewRouterWithRealRentingAdapter(options);

        // Camino de producción: IEmailSender.SendAsync(EmailMessage, ct) — el "método de una vía".
        var result = await router.SendAsync(ProductionMessage(OriginalRecipientEmail), Ct);

        result.Success.Should().BeTrue();
        result.RecipientDiverted.Should().BeTrue(
            "el camino de producción NUNCA propaga la exención: el desvío obligatorio de la HU #11364 sigue vigente");

        handler.LastRequestParts.Should().NotBeNull();
        handler.LastRequestParts!.Should().ContainKey("Recipients[0].Email").WhoseValue.Should().Be(DivertedRecipientEmail);
        handler.LastRequestParts!.Should().NotContainValue(OriginalRecipientEmail);
    }

    [Fact]
    public async Task CaminoDeProduccion_ConTenantApiYDesvioActivo_ReemplazaDestinatariosPrincipalesYCopiaOculta()
    {
        // AC1 de la HU #11364, ejercitado de punta a punta (router real + adaptador real + desvío
        // real): la petición que construye el router para un solo destinatario no lleva copia
        // oculta, así que este test confirma el contrato a nivel de RentingSendEmailRequest
        // (RentingRecipientOverrideTests ya cubre el caso con BCC poblado); aquí se confirma que,
        // en la cadena completa, NINGÚN campo de destinatario conserva el correo original.
        var options = NewOptions(overrideEnabled: true);
        var (router, handler) = NewRouterWithRealRentingAdapter(options);

        await router.SendAsync(ProductionMessage(OriginalRecipientEmail), Ct);

        handler.LastRequestParts!.Values.Should().NotContain(OriginalRecipientEmail);
    }

    // ── Guardarraíl estructural: el método de una vía no tiene forma de propagar la exención ───

    [Fact]
    public void IEmailSender_SendAsync_NoTieneNingunParametroDeTipoControlledMailboxRecipient()
    {
        // Regresión estructural (HU #11372): si alguien intentara colar la exención en el camino de
        // producción, tendría que añadir un parámetro a este método. Que la firma NUNCA lo tenga es
        // la prueba de que es imposible por construcción, no una convención.
        var method = typeof(IEmailSender).GetMethod(nameof(IEmailSender.SendAsync));

        method.Should().NotBeNull();
        method!.GetParameters().Should().NotContain(p => p.ParameterType == typeof(ControlledMailboxRecipient));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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
        /// Partes del último multipart, capturadas ANTES de responder (el sender dispone la
        /// petición apenas termina el envío) — mismo patrón que <c>RentingEmailApiSenderTests</c>.
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
}
