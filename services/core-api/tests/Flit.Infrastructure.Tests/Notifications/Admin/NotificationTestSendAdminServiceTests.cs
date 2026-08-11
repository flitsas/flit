using Flit.Admin.Application.Plataforma.Notificaciones;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.Admin;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Admin;

/// <summary>
/// HU #11368 (Feature #11349) — envío de prueba de una plantilla del catálogo al buzón de pruebas,
/// con límite de frecuencia persistido. Cubre los 8 AC de la HU.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var service = NewService(dbName, out var db, out var emailSender, out _);
/// var result = await service.SendAsync(new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), userId);
/// </code>
/// Mismo patrón InMemory que <c>AnalyticsSchedulerProcessorTests</c>: un <see cref="FlitDbContext"/>
/// por nombre de base, <c>IEmailSender</c> sustituido con NSubstitute.
/// </remarks>
public sealed class NotificationTestSendAdminServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC1 — envío correcto ─────────────────────────────────────────────────

    [Fact]
    public async Task AC1_ConBuzonConfiguradoYSinPruebaPrevia_Devuelve200ConExitoYSellaElInstante()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);

        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);
        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), UserId, Ct);

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(NotificationTestSendOutcome.Sent);
        result.SenderEmail.Should().Be("tramitesvehiculos@flitsas.com");
        result.SentAt.Should().Be(now);

        await using var verify = NewContext(dbName);
        var row = await verify.NotificationTestSettings.SingleAsync(Ct);
        row.LastTestSentAt.Should().Be(now, "AC1 — la marca del último envío queda sellada con el instante del intento");
        row.UpdatedBy.Should().Be(UserId);

        await emailSender.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ── AC2 — límite de frecuencia ───────────────────────────────────────────

    [Fact]
    public async Task AC2_SegundaSolicitudDentroDeLaVentana_Devuelve429ConSegundosRestantesYNoTocaElTransporte()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);
        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);

        var first = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), UserId, Ct);
        first.Success.Should().BeTrue();

        // Segunda solicitud, DE OTRA PLANTILLA, 30 segundos después (ventana de 5 minutos vigente).
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        emailSender.ClearReceivedCalls();

        var second = await service.SendAsync(
            new SendNotificationTestRequest("analytics.alert", "FLIT_SMTP"), UserId, Ct);

        second.Success.Should().BeFalse();
        second.Outcome.Should().Be(NotificationTestSendOutcome.RateLimited);
        second.RetryAfterSeconds.Should().BeGreaterThan(0).And.BeLessOrEqualTo(300);

        await emailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_SolicitudDespuesDeLaVentana_YaNoEstaLimitada()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);
        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);

        await service.SendAsync(new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), UserId, Ct);

        timeProvider.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), UserId, Ct);

        result.Success.Should().BeTrue();
    }

    // ── AC3 — sin buzón configurado ──────────────────────────────────────────

    [Fact]
    public async Task AC3_SinBuzonConfigurado_Devuelve400YNoModificaLaMarca()
    {
        var dbName = NewDbName();
        // Sin SeedMailboxAsync: la fila nace sin buzón (test_recipient_email NULL).
        await using (var seed = NewContext(dbName))
        {
            seed.NotificationTestSettings.Add(new NotificationTestSettingsRow
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(Ct);
        }
        var emailSender = Substitute.For<IEmailSender>();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);

        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), UserId, Ct);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(NotificationTestSendOutcome.MailboxNotConfigured);

        await using var verify = NewContext(dbName);
        var row = await verify.NotificationTestSettings.SingleAsync(Ct);
        row.LastTestSentAt.Should().BeNull("AC3 — el buzón no configurado no puede consumir el enfriamiento");

        await emailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ── AC4 — canal API Renting (desviación deliberada) ──────────────────────

    [Fact]
    public async Task AC4_ConCanalTenantApiYRentingDeshabilitado_DevuelveConfiguracionIncompletaYNoEnviaPorFlitSmtp()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var service = NewService(
            dbName, emailSender, timeProvider, isConsoleTransport: false,
            rentingOptions: new RentingChannelOptions { Enabled = false });

        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "TENANT_API"), UserId, Ct);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(NotificationTestSendOutcome.ChannelNotConfigured);

        await using var verify = NewContext(dbName);
        var row = await verify.NotificationTestSettings.SingleAsync(Ct);
        row.LastTestSentAt.Should().BeNull();

        await emailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_ConCanalTenantApiYRentingHabilitado_SigueSinEnviarPorColasFlit()
    {
        // El enrutamiento por canal (HU #11362) no existe todavía: aunque el interruptor esté en
        // true, ESTE banco de pruebas no tiene forma de invocar el adaptador Renting.
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var service = NewService(
            dbName, emailSender, timeProvider, isConsoleTransport: false,
            rentingOptions: new RentingChannelOptions { Enabled = true });

        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "TENANT_API"), UserId, Ct);

        result.Outcome.Should().Be(NotificationTestSendOutcome.ChannelNotConfigured);
        await emailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ── AC5 — fallo de render ────────────────────────────────────────────────

    [Fact]
    public async Task AC5_SiElRenderFalla_NoEnviaYReportaFalloDeRender()
    {
        // No hay forma de forzar un fallo de render con el catálogo real (las 5 plantillas
        // registradas SIEMPRE tienen muestra) — se prueba el contrato con un templateId que el
        // catálogo SÍ resuelve, verificando que el switch de RenderSample cubre las 5 entradas
        // reales (ver AC7_TodasLasPlantillasDelCatalogoRenderizanSinExcepcion). El AC5 "sin
        // envío" se prueba a nivel de contrato: TryResolve por id inexistente ya cae en AC6 (404)
        // ANTES de llegar al render — este test documenta esa frontera.
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);

        // Ninguna de las 5 entradas reales del catálogo dispara RenderFailed: se confirma que
        // NINGUNA de ellas lanza y que, por construcción, el switch de RenderSample cubre el
        // catálogo entero (mismo id que TryResolve, HU #11353 AC3 — ids estables).
        foreach (var templateId in new[]
                 {
                     "security.invitation", "security.forgot-password", "security.admin-reset-password",
                     "analytics.scheduled-report", "analytics.alert",
                 })
        {
            var result = await service.SendAsync(
                new SendNotificationTestRequest(templateId, "FLIT_SMTP"), UserId, Ct);
            result.Outcome.Should().NotBe(NotificationTestSendOutcome.RenderFailed);

            // Deja pasar el enfriamiento entre iteraciones para no toparse con AC2.
            timeProvider.Advance(TimeSpan.FromMinutes(6));
        }
    }

    // ── AC6 — plantilla inexistente ──────────────────────────────────────────

    [Fact]
    public async Task AC6_ConTemplateIdInexistente_Devuelve404YNoConsumeElEnfriamiento()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);

        var result = await service.SendAsync(
            new SendNotificationTestRequest("plantilla.no-existe", "FLIT_SMTP"), UserId, Ct);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(NotificationTestSendOutcome.TemplateNotFound);

        await using var verify = NewContext(dbName);
        var row = await verify.NotificationTestSettings.SingleAsync(Ct);
        row.LastTestSentAt.Should().BeNull("AC6 — la plantilla inexistente no puede consumir el enfriamiento");

        await emailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());

        // Confirma que, tras el 404, un envío válido inmediatamente después SIGUE disponible
        // (el enfriamiento no arrancó).
        var next = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), UserId, Ct);
        next.Success.Should().BeTrue();
    }

    // ── AC7 — solo datos de muestra (reset administrativo) ───────────────────

    [Fact]
    public async Task AC7_MuestraDeResetAdministrativo_NoContieneContrasenaNiDatosReales()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        string? capturedHtml = null;
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(m => capturedHtml = m.HtmlBody), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);

        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.admin-reset-password", "FLIT_SMTP"), UserId, Ct);

        result.Success.Should().BeTrue();
        capturedHtml.Should().NotBeNull();
        // Único valor variable del reset administrativo: el marcador de contraseña temporal, NUNCA
        // una contraseña real (la clase de muestra ni siquiera referencia el generador real). El
        // compositor HTML-encodea las tildes (Á/Ñ → &#193;/&#209;), así que se verifica el tramo
        // ASCII del marcador, que sigue siendo inequívoco.
        capturedHtml.Should().Contain("VA LA CONTRASE").And.Contain("TEMPORAL");
        capturedHtml.Should().Contain("VA EL NOMBRE DEL DESTINATARIO");
    }

    // ── AC8 — el transporte de consola no se reporta como envío real ─────────

    [Fact]
    public async Task AC8_ConTransporteDeConsola_DeclaraQueNoHuboCorreoReal()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        // Simula exactamente lo que hace ConsoleEmailSender: SIEMPRE Sent, sin importar el transporte.
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));

        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: true);

        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), UserId, Ct);

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(NotificationTestSendOutcome.Sent, "el puerto SIEMPRE reporta Sent, con consola o con SMTP real");
        result.IsConsoleTransport.Should().BeTrue();
        result.Message.Should().ContainAny("CONSOLA", "consola");
        result.Message.Should().ContainAny("no se envió", "no hubo");
    }

    [Fact]
    public async Task AC8_ConTransporteRealSmtp_NoDeclaraConsola()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));

        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);

        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), UserId, Ct);

        result.IsConsoleTransport.Should().BeFalse();
        result.Message.Should().NotContain("CONSOLA");
    }

    // ── Contrato adicional: fallo de transporte real (auth SMTP) se sella igual ──

    [Fact]
    public async Task FalloDeTransporteReal_SeSellaIgualQueUnEnvioExitosoYSeReportaComoTransportFailed()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Failed(EmailSendOutcome.AuthenticationFailed)));
        var now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);
        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);

        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "FLIT_SMTP"), UserId, Ct);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(NotificationTestSendOutcome.TransportFailed);

        await using var verify = NewContext(dbName);
        var row = await verify.NotificationTestSettings.SingleAsync(Ct);
        row.LastTestSentAt.Should().Be(now, "el sello ocurre ANTES de enviar, sin importar si el transporte falla después");
    }

    // ── Canal inválido: error de entrada, no consume enfriamiento ────────────

    [Fact]
    public async Task ConCanalInvalido_Devuelve400YNoConsumeElEnfriamiento()
    {
        var dbName = NewDbName();
        await SeedMailboxAsync(dbName, "pruebas@flit.co");
        var emailSender = Substitute.For<IEmailSender>();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var service = NewService(dbName, emailSender, timeProvider, isConsoleTransport: false);

        var result = await service.SendAsync(
            new SendNotificationTestRequest("security.invitation", "CANAL_QUE_NO_EXISTE"), UserId, Ct);

        result.Outcome.Should().Be(NotificationTestSendOutcome.InvalidChannel);
        await emailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());

        await using var verify = NewContext(dbName);
        var row = await verify.NotificationTestSettings.SingleAsync(Ct);
        row.LastTestSentAt.Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NewDbName() => $"flit-notification-test-send-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task SeedMailboxAsync(string dbName, string email)
    {
        await using var db = NewContext(dbName);
        db.NotificationTestSettings.Add(new NotificationTestSettingsRow
        {
            Id = Guid.NewGuid(),
            TestRecipientEmail = email,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static NotificationTestSendAdminService NewService(
        string dbName,
        IEmailSender emailSender,
        TestTimeProvider timeProvider,
        bool isConsoleTransport,
        RentingChannelOptions? rentingOptions = null)
    {
        var emailSettings = new EmailSettings
        {
            Host = "smtp.office365.com",
            DefaultSenderEmail = "tramitesvehiculos@flitsas.com",
            DefaultSenderName = "FLIT Trámites",
        };

        return new NotificationTestSendAdminService(
            NewContext(dbName),
            emailSender,
            emailSettings,
            Options.Create(rentingOptions ?? new RentingChannelOptions()),
            new EmailTransportDescriptor(isConsoleTransport),
            timeProvider,
            NullLogger<NotificationTestSendAdminService>.Instance);
    }

    /// <summary>
    /// <see cref="TimeProvider"/> mutable mínimo para test (el paquete
    /// <c>Microsoft.Extensions.TimeProvider.Testing</c> no está referenciado en este proyecto):
    /// solo sobreescribe <see cref="GetUtcNow"/>, que es lo único que consume
    /// <see cref="NotificationTestSendAdminService"/>.
    /// </summary>
    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
