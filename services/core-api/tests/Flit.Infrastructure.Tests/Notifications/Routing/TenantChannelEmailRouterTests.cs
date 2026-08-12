using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Routing;

/// <summary>
/// HU #11362 (Feature #11348, cierra Bug #11311) — enrutamiento por canal del tenant, remitente por
/// canal y desacople del interruptor de documentos personalizados.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var router = new TenantChannelEmailRouter(
///     flitTransport, tenantSettingsRepository, rentingSender, rentingOptions, logger);
/// var result = await router.SendAsync(message, CancellationToken.None);
/// </code>
/// </remarks>
public sealed class TenantChannelEmailRouterTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-2222-4000-8000-000000000001");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TenantSettings Settings(NotificationChannel channel) => new()
    {
        TenantId = TenantId,
        AllowInitialRegistration = true,
        AllowMiscNewVehicles = true,
        OnlyOwnVehicles = false,
        SignatureVaultEnabled = false,
        NotificationChannel = channel,
        NotificationTarget = NotificationTarget.Radicador,
        PaymentMethods = [],
    };

    // "sin especificar" (usa TenantId) se distingue de "explícitamente null" con un booleano: si se
    // usara `tenantId ?? TenantId`, un llamador que pase null a propósito (mensaje sin tenant
    // resoluble) terminaría con TenantId igual — exactamente lo que este helper no debe hacer.
    private static EmailMessage AccountEmail(string templateKey = "security.forgot-password", Guid? tenantId = null, bool explicitTenantId = false) =>
        new(explicitTenantId ? tenantId : tenantId ?? TenantId, templateKey, "destino@example.test", "Destino", "Asunto", "<p>Cuerpo</p>");

    private static EmailMessage AnalyticsEmail(string templateKey = "analytics.scheduled-report", Guid? tenantId = null, bool explicitTenantId = false) =>
        new(explicitTenantId ? tenantId : tenantId ?? TenantId, templateKey, "destino@example.test", "Destino", "Asunto", "<p>Cuerpo</p>");

    private static IOptions<RentingChannelOptions> RentingOptions(string senderEmail = "renting@example.test", string senderUser = "Renting") =>
        Options.Create(new RentingChannelOptions
        {
            Enabled = true,
            SendEmailSenderEmail = senderEmail,
            SendEmailSenderUsername = senderUser,
        });

    private static TenantChannelEmailRouter NewRouter(
        IEmailSender flitTransport,
        ITenantSettingsRepository settingsRepository,
        IRentingEmailApiSender? rentingSender = null,
        IOptions<RentingChannelOptions>? rentingOptions = null) =>
        new(
            flitTransport,
            settingsRepository,
            rentingSender,
            rentingOptions ?? RentingOptions(),
            NullLogger<TenantChannelEmailRouter>.Instance);

    // ---------- AC1: tenant con canal TenantApi sale por el adaptador del cliente ----------

    [Fact]
    public async Task AC1_TenantApiChannel_AnalyticsEmail_RoutesToRentingAdapter_WithChannelSender()
    {
        var settingsRepo = Substitute.For<ITenantSettingsRepository>();
        settingsRepo.GetAsync(TenantId, Ct).Returns(Settings(NotificationChannel.TenantApi));
        var flitTransport = Substitute.For<IEmailSender>();
        var rentingSender = Substitute.For<IRentingEmailApiSender>();
        rentingSender.SendAsync(Arg.Any<RentingSendEmailRequest>(), Ct).Returns(EmailSendResult.Sent);

        var router = NewRouter(flitTransport, settingsRepo, rentingSender, RentingOptions("canal@renting.test", "Canal Renting"));

        var result = await router.SendAsync(AnalyticsEmail(), Ct);

        result.Success.Should().BeTrue();
        // Bug corregido — la bitácora (HU #11363) necesita el canal REALMENTE usado, no una
        // constante fija: aquí el router lo etiqueta como TenantApi.
        result.Channel.Should().Be(TenantSettingsCodes.ChannelTenantApi);
        await flitTransport.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await rentingSender.Received(1).SendAsync(
            Arg.Is<RentingSendEmailRequest>(r =>
                r.Sender.Email == "canal@renting.test" && r.Sender.UserName == "Canal Renting"),
            Ct);

        // HU #11372 — el camino de producción (SendAsync(EmailMessage, ...)) NUNCA propaga la
        // exención de buzón controlado: solo debe llamarse la sobrecarga de 2 parámetros.
        await rentingSender.DidNotReceive().SendAsync(
            Arg.Any<RentingSendEmailRequest>(), Arg.Any<ControlledMailboxRecipient>(), Arg.Any<CancellationToken>());
    }

    // ---------- HU #11372: solo el camino explícito (banco de pruebas) propaga la exención ----------

    [Fact]
    public async Task ExplicitChannelSendAsync_ConTenantApi_PropagaLaExencionDeBuzonControlado()
    {
        var settingsRepo = Substitute.For<ITenantSettingsRepository>();
        var flitTransport = Substitute.For<IEmailSender>();
        var rentingSender = Substitute.For<IRentingEmailApiSender>();
        rentingSender.SendAsync(
            Arg.Any<RentingSendEmailRequest>(), Arg.Any<ControlledMailboxRecipient>(), Ct).Returns(EmailSendResult.Sent);

        var router = NewRouter(
            flitTransport, settingsRepo, rentingSender, RentingOptions("canal@renting.test", "Canal Renting"));

        var message = new EmailMessage(
            TenantId: null, "analytics.alert", "buzon-pruebas@example.test", "Banco de pruebas", "Asunto", "<p>Cuerpo</p>");

        // Camino explícito del banco de pruebas (HU #11371): SendAsync(NotificationChannel, ...).
        var result = await router.SendAsync(NotificationChannel.TenantApi, message, Ct);

        result.Success.Should().BeTrue();
        await rentingSender.Received(1).SendAsync(
            Arg.Any<RentingSendEmailRequest>(), Arg.Any<ControlledMailboxRecipient>(), Ct);
        // Nunca debe caer en la sobrecarga sin exención: eso volvería a exponer el buzón al desvío.
        await rentingSender.DidNotReceive().SendAsync(Arg.Any<RentingSendEmailRequest>(), Arg.Any<CancellationToken>());
        // La política del tenant no se consulta (AC de la HU #11371: canal explícito, no resuelto).
        await settingsRepo.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ---------- AC2: tenant con canal FlitSmtp sale por SMTP ----------

    [Fact]
    public async Task AC2_FlitSmtpChannel_AnalyticsEmail_RoutesToFlitTransport()
    {
        var settingsRepo = Substitute.For<ITenantSettingsRepository>();
        settingsRepo.GetAsync(TenantId, Ct).Returns(Settings(NotificationChannel.FlitSmtp));
        var flitTransport = Substitute.For<IEmailSender>();
        flitTransport.SendAsync(Arg.Any<EmailMessage>(), Ct).Returns(EmailSendResult.Sent);
        var rentingSender = Substitute.For<IRentingEmailApiSender>();

        var router = NewRouter(flitTransport, settingsRepo, rentingSender);
        var message = AnalyticsEmail();

        var result = await router.SendAsync(message, Ct);

        result.Success.Should().BeTrue();
        result.Channel.Should().Be(TenantSettingsCodes.ChannelFlitSmtp);
        await flitTransport.Received(1).SendAsync(message, Ct);
        await rentingSender.DidNotReceive().SendAsync(Arg.Any<RentingSendEmailRequest>(), Arg.Any<CancellationToken>());
    }

    // ---------- AC3: los cuatro correos de cuenta ignoran el canal ----------

    [Theory]
    [InlineData("security.invitation")]
    [InlineData("security.forgot-password")]
    [InlineData("security.admin-reset-password")]
    public async Task AC3_AccountEmails_AlwaysUseFlitTransport_EvenWithTenantApiChannel(string templateKey)
    {
        var settingsRepo = Substitute.For<ITenantSettingsRepository>();
        settingsRepo.GetAsync(TenantId, Ct).Returns(Settings(NotificationChannel.TenantApi));
        var flitTransport = Substitute.For<IEmailSender>();
        flitTransport.SendAsync(Arg.Any<EmailMessage>(), Ct).Returns(EmailSendResult.Sent);
        var rentingSender = Substitute.For<IRentingEmailApiSender>();

        var router = NewRouter(flitTransport, settingsRepo, rentingSender);
        var message = AccountEmail(templateKey);

        var result = await router.SendAsync(message, Ct);

        result.Success.Should().BeTrue();
        // Bug corregido — pese a que el tenant tiene TenantApi configurado, AC3 hace que el correo
        // de cuenta salga por FlitSmtp: el canal EFECTIVO (el que ve la bitácora) es flit_smtp, no
        // el del tenant.
        result.Channel.Should().Be(TenantSettingsCodes.ChannelFlitSmtp);
        await flitTransport.Received(1).SendAsync(message, Ct);
        await rentingSender.DidNotReceive().SendAsync(Arg.Any<RentingSendEmailRequest>(), Arg.Any<CancellationToken>());
        // El repositorio ni siquiera necesita consultarse para bypasear el canal.
        await settingsRepo.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ---------- AC6: tenant sin política operativa usa SMTP de FLIT por defecto ----------

    [Fact]
    public async Task AC6_TenantWithoutOperationalPolicy_DefaultsToFlitTransport()
    {
        var settingsRepo = Substitute.For<ITenantSettingsRepository>();
        settingsRepo.GetAsync(TenantId, Ct).Returns((TenantSettings?)null);
        var flitTransport = Substitute.For<IEmailSender>();
        flitTransport.SendAsync(Arg.Any<EmailMessage>(), Ct).Returns(EmailSendResult.Sent);
        var rentingSender = Substitute.For<IRentingEmailApiSender>();

        var router = NewRouter(flitTransport, settingsRepo, rentingSender);
        var message = AnalyticsEmail();

        var result = await router.SendAsync(message, Ct);

        result.Success.Should().BeTrue();
        await flitTransport.Received(1).SendAsync(message, Ct);
        await rentingSender.DidNotReceive().SendAsync(Arg.Any<RentingSendEmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_MessageWithoutResolvableTenant_DefaultsToFlitTransport_WithoutQueryingSettings()
    {
        var settingsRepo = Substitute.For<ITenantSettingsRepository>();
        var flitTransport = Substitute.For<IEmailSender>();
        flitTransport.SendAsync(Arg.Any<EmailMessage>(), Ct).Returns(EmailSendResult.Sent);
        var rentingSender = Substitute.For<IRentingEmailApiSender>();

        var router = NewRouter(flitTransport, settingsRepo, rentingSender);
        var message = AnalyticsEmail(tenantId: null, explicitTenantId: true);

        var result = await router.SendAsync(message, Ct);

        result.Success.Should().BeTrue();
        await flitTransport.Received(1).SendAsync(message, Ct);
        await settingsRepo.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ---------- Caso heredado de la HU #11359 AC6: canal TenantApi pero Renting NO habilitado ----------

    [Fact]
    public async Task TenantApiChannel_RentingNotAvailable_ReturnsConfigurationIncomplete_NeverFallsBackToSmtp()
    {
        var settingsRepo = Substitute.For<ITenantSettingsRepository>();
        settingsRepo.GetAsync(TenantId, Ct).Returns(Settings(NotificationChannel.TenantApi));
        var flitTransport = Substitute.For<IEmailSender>();

        // IRentingEmailApiSender null: mismo estado que produce InfrastructureExtensions cuando el
        // canal Renting no está habilitado por configuración (RENTING_API_ENABLED != true).
        var router = NewRouter(flitTransport, settingsRepo, rentingSender: null);

        var result = await router.SendAsync(AnalyticsEmail(), Ct);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(EmailSendOutcome.ConfigurationIncomplete);
        // El canal intentado fue TenantApi (nunca cayó a SMTP en silencio) — la bitácora debe
        // reflejar eso, no flit_smtp.
        result.Channel.Should().Be(TenantSettingsCodes.ChannelTenantApi);
        await flitTransport.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ---------- Contrato adicional del router (no un AC numerado de la HU): plantilla desconocida
    // no gana el bypass de AC3 ni se trata como correo de cuenta ----------

    [Fact]
    public async Task UnknownTemplateKey_IsNotTreatedAsAccountEmail_StillRoutesByChannel()
    {
        var settingsRepo = Substitute.For<ITenantSettingsRepository>();
        settingsRepo.GetAsync(TenantId, Ct).Returns(Settings(NotificationChannel.TenantApi));
        var flitTransport = Substitute.For<IEmailSender>();
        var rentingSender = Substitute.For<IRentingEmailApiSender>();
        rentingSender.SendAsync(Arg.Any<RentingSendEmailRequest>(), Ct).Returns(EmailSendResult.Sent);

        var router = NewRouter(flitTransport, settingsRepo, rentingSender);
        var message = AnalyticsEmail(templateKey: "modulo-futuro.no-catalogado");

        var result = await router.SendAsync(message, Ct);

        result.Success.Should().BeTrue();
        await rentingSender.Received(1).SendAsync(Arg.Any<RentingSendEmailRequest>(), Ct);
        await flitTransport.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }
}
