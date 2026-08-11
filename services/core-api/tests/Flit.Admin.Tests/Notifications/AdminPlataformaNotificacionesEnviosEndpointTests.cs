using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Tests.Companies;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Notifications;

/// <summary>
/// Colección xUnit compartida por todas las suites que mutan la fila singleton
/// <c>admin.notification_test_settings</c> (HU #11365/#11366/#11368) — las serializa entre sí
/// (xUnit no paraleliza clases de la misma colección) para que ninguna sobreescriba el buzón o la
/// marca de enfriamiento que otra está verificando a mitad de test.
/// </summary>
[CollectionDefinition(Name)]
public sealed class NotificationTestSettingsSingletonCollection
{
    public const string Name = "NotificationTestSettingsSingleton";
}

/// <summary>
/// HU #11368 (Feature #11349) — <c>POST /buzon-pruebas/envios</c>: contrato HTTP del envío de
/// prueba (autorización + mapeo de causas a códigos de estado). La lógica de negocio (los 8 AC
/// completos, incluida la ventana de enfriamiento y el AC8 de transporte de consola) está cubierta
/// a nivel de servicio en <c>Flit.Infrastructure.Tests.Notifications.Admin.NotificationTestSendAdminServiceTests</c>
/// — aquí solo se confirma que el endpoint traduce cada desenlace al HTTP correcto.
/// </summary>
[Collection(NotificationTestSettingsSingletonCollection.Name)]
public sealed class AdminPlataformaNotificacionesEnviosEndpointTests
    : IClassFixture<EnviosTestFactory>, IDisposable
{
    private const string EnviosUrl = "/api/v1/admin/plataforma/notificaciones/buzon-pruebas/envios";

    private readonly EnviosTestFactory _factory;
    private readonly HttpClient _client;

    public AdminPlataformaNotificacionesEnviosEndpointTests(EnviosTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.ExplicitChannelEmailSender.ClearReceivedCalls();
        // Restablece la disponibilidad por defecto en cada test (el doble es compartido vía
        // IClassFixture): FlitSmtp siempre disponible, TenantApi apagado — mismo estado que el
        // ambiente real de pruebas (RENTING_API_ENABLED no definida).
        _factory.ExplicitChannelEmailSender.IsChannelAvailable(NotificationChannel.FlitSmtp).Returns(true);
        _factory.ExplicitChannelEmailSender.IsChannelAvailable(NotificationChannel.TenantApi).Returns(false);
        ResetRow();
    }

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            EnviosUrl, new { templateId = "security.invitation", channel = "FLIT_SMTP" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithNonSuperAdminRole_Returns403()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PostAsJsonAsync(
            EnviosUrl, new { templateId = "security.invitation", channel = "FLIT_SMTP" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC1_ConBuzonConfigurado_Returns200ConExito()
    {
        SetMailbox("pruebas-envio@flit.co");
        _factory.ExplicitChannelEmailSender
            .SendAsync(NotificationChannel.FlitSmtp, Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));

        var response = await SuperAdminClient().PostAsJsonAsync(
            EnviosUrl, new { templateId = "security.invitation", channel = "FLIT_SMTP" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Outcome.Should().Be("Sent");
        body.SenderEmail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AC3_SinBuzonConfigurado_Returns400ConCausaBuzonNoConfigurado()
    {
        ResetRow(); // deja el buzón sin configurar

        var response = await SuperAdminClient().PostAsJsonAsync(
            EnviosUrl, new { templateId = "security.invitation", channel = "FLIT_SMTP" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorDto>(TestContext.Current.CancellationToken);
        body!.Error.Should().Be("buzon_no_configurado");

        await _factory.ExplicitChannelEmailSender.DidNotReceive().SendAsync(
            Arg.Any<NotificationChannel>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_ConPlantillaInexistente_Returns404()
    {
        SetMailbox("pruebas-envio-ac6@flit.co");

        var response = await SuperAdminClient().PostAsJsonAsync(
            EnviosUrl, new { templateId = "plantilla.no-existe", channel = "FLIT_SMTP" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConCanalInvalido_Returns400ConCausaCanalInvalido()
    {
        SetMailbox("pruebas-envio-canal@flit.co");

        var response = await SuperAdminClient().PostAsJsonAsync(
            EnviosUrl, new { templateId = "security.invitation", channel = "NO_EXISTE" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorDto>(TestContext.Current.CancellationToken);
        body!.Error.Should().Be("canal_invalido");
    }

    [Fact]
    public async Task AC2_SegundaSolicitudDentroDeLaVentana_Returns429()
    {
        SetMailbox("pruebas-envio-ac2@flit.co");
        _factory.ExplicitChannelEmailSender
            .SendAsync(NotificationChannel.FlitSmtp, Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var client = SuperAdminClient();

        var first = await client.PostAsJsonAsync(
            EnviosUrl, new { templateId = "security.invitation", channel = "FLIT_SMTP" },
            TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.ExplicitChannelEmailSender.ClearReceivedCalls();
        var second = await client.PostAsJsonAsync(
            EnviosUrl, new { templateId = "analytics.alert", channel = "FLIT_SMTP" },
            TestContext.Current.CancellationToken);

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await second.Content.ReadFromJsonAsync<RateLimitedDto>(TestContext.Current.CancellationToken);
        body!.Error.Should().Be("limite_frecuencia");
        body.RetryAfterSeconds.Should().BeGreaterThan(0);

        await _factory.ExplicitChannelEmailSender.DidNotReceive().SendAsync(
            Arg.Any<NotificationChannel>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_ConCanalTenantApiNoDisponible_Returns400ConCausaConfiguracionIncompleta()
    {
        // HU #11371 — "security.invitation" es una plantilla de cuenta: con TENANT_API caería en
        // TemplateChannelMismatch (400 plantilla_sin_enrutamiento_por_canal), no en
        // "configuracion_incompleta". Este AC prueba la disponibilidad del canal, así que usa una
        // plantilla de analítica — el _factory.ExplicitChannelEmailSender NO tiene IsChannelAvailable
        // configurado para TENANT_API (default NSubstitute = false), igual que el adaptador Renting
        // deshabilitado en este ambiente.
        SetMailbox("pruebas-envio-ac4@flit.co");

        var response = await SuperAdminClient().PostAsJsonAsync(
            EnviosUrl, new { templateId = "analytics.alert", channel = "TENANT_API" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorDto>(TestContext.Current.CancellationToken);
        body!.Error.Should().Be("configuracion_incompleta");

        await _factory.ExplicitChannelEmailSender.DidNotReceive().SendAsync(
            Arg.Any<NotificationChannel>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HU11371_PlantillaDeCuentaConTenantApi_Returns400ConCausaPlantillaSinEnrutamientoPorCanal()
    {
        SetMailbox("pruebas-envio-hu11371@flit.co");

        var response = await SuperAdminClient().PostAsJsonAsync(
            EnviosUrl, new { templateId = "security.invitation", channel = "TENANT_API" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorDto>(TestContext.Current.CancellationToken);
        body!.Error.Should().Be("plantilla_sin_enrutamiento_por_canal");

        await _factory.ExplicitChannelEmailSender.DidNotReceive().SendAsync(
            Arg.Any<NotificationChannel>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HU11371_PlantillaDeAnalitica_ConTenantApiDisponible_Returns200YEnviaPorElAdaptador()
    {
        // Factory DEDICADA (no la _factory compartida vía IClassFixture): "IsChannelAvailable" es
        // estado mutable de un único doble compartido por TODOS los métodos de esta clase. xUnit
        // v3 puede ejecutar los [Fact] de una misma clase en paralelo, así que reconfigurar el doble
        // compartido aquí (TenantApi=true) puede pisar — o ser pisado por — el reset por defecto
        // (TenantApi=false) del constructor de OTRO método corriendo al mismo tiempo. Una factory
        // propia aísla el doble sin tocar el estado que usan las demás pruebas de la clase.
        using var dedicatedFactory = new EnviosTestFactory();
        dedicatedFactory.ExplicitChannelEmailSender.IsChannelAvailable(NotificationChannel.FlitSmtp).Returns(true);
        dedicatedFactory.ExplicitChannelEmailSender.IsChannelAvailable(NotificationChannel.TenantApi).Returns(true);
        dedicatedFactory.ExplicitChannelEmailSender
            .SendAsync(NotificationChannel.TenantApi, Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));

        using (var scope = dedicatedFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            var row = db.NotificationTestSettings.SingleOrDefault();
            if (row is null)
            {
                row = new NotificationTestSettingsRow { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
                db.NotificationTestSettings.Add(row);
            }

            row.TestRecipientEmail = "pruebas-envio-hu11371-tenantapi@flit.co";
            row.LastTestSentAt = null;
            db.SaveChanges();
        }

        using var client = dedicatedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.PostAsJsonAsync(
            EnviosUrl, new { templateId = "analytics.alert", channel = "TENANT_API" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendDto>(TestContext.Current.CancellationToken);
        body!.Success.Should().BeTrue();

        await dedicatedFactory.ExplicitChannelEmailSender.Received(1).SendAsync(
            NotificationChannel.TenantApi, Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await dedicatedFactory.ExplicitChannelEmailSender.DidNotReceive().SendAsync(
            NotificationChannel.FlitSmtp, Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient SuperAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        return client;
    }

    private void SetMailbox(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = db.NotificationTestSettings.Single();
        row.TestRecipientEmail = email;
        row.LastTestSentAt = null;
        db.SaveChanges();
    }

    private void ResetRow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = db.NotificationTestSettings.SingleOrDefault();
        if (row is null)
        {
            row = new NotificationTestSettingsRow { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
            db.NotificationTestSettings.Add(row);
        }

        row.TestRecipientEmail = null;
        row.LastTestSentAt = null;
        row.UpdatedAt = null;
        row.UpdatedBy = null;
        db.SaveChanges();
    }

    public void Dispose()
    {
        try
        {
            ResetRow();
        }
        catch
        {
            // Housekeeping best-effort: la BD de desarrollo es compartida.
        }
    }

    private sealed record SendDto(
        bool Success, string Outcome, string Message, string? TemplateId, string? Channel,
        string? SenderEmail, string? SenderName, DateTimeOffset? SentAt, bool IsConsoleTransport,
        bool RecipientDiverted);

    private sealed record ErrorDto(string Error, string? Message);

    private sealed record RateLimitedDto(string Error, string? Message, int? RetryAfterSeconds);
}

/// <summary>
/// Factory que sustituye <see cref="IExplicitChannelEmailSender"/> por un doble NSubstitute — mismo
/// patrón que <c>TransferStartEndpointTests.TransferTestFactory</c> — para que esta suite nunca
/// intente una conexión SMTP/Renting real. HU #11371: <c>NotificationTestSendAdminService</c> ya no
/// depende de <see cref="IEmailSender"/> — llama directamente al enrutador vía
/// <see cref="IExplicitChannelEmailSender"/>, así que ESTE es el punto de sustitución correcto
/// (sustituir <c>IEmailSender</c> aquí no tendría ningún efecto sobre el banco de pruebas).
/// </summary>
/// <remarks>
/// Corrección post-CI (2026-08-11, dos rondas): esta suite dependía de configuración de AMBIENTE
/// (<c>appsettings.Development.json</c>, gitignored) para el remitente SMTP, el transporte
/// consola-vs-SMTP y las opciones del canal Renting — presente en algunas máquinas, ausente en CI,
/// así que el resultado divergía entre ambos. La primera corrección usó
/// <c>builder.ConfigureAppConfiguration</c> para el remitente SMTP, pero eso llega TARDE: en
/// <c>InfrastructureExtensions.AddPostgresInfrastructure</c>, <c>EmailSettings</c> se lee de
/// <c>IConfiguration</c> y se registra como instancia YA MATERIALIZADA
/// (<c>services.AddSingleton(emailSettings)</c>) durante <c>Program.cs</c>, ANTES de que
/// <c>WebApplicationFactory</c> aplique los callbacks de <c>ConfigureAppConfiguration</c> sobre el
/// <c>IWebHostBuilder</c> — la colección en memoria nunca llega a influir en ese objeto. Ahora se
/// registra el <c>EmailSettings</c> de prueba directamente como servicio dentro de
/// <c>ConfigureTestServices</c> (se ejecuta al final del pipeline de hosting; la última
/// registración de un servicio único gana al resolver), la MISMA técnica que ya funcionaba para
/// <see cref="RentingChannelOptions"/>:
/// <list type="bullet">
/// <item><description><c>EmailSettings.DefaultSenderEmail</c>/<c>DefaultSenderName</c> — remitente
/// que lee <c>ResolveSender(FlitSmtp)</c>; vacío en <c>appsettings.json</c> (el único que existe en
/// CI) ⇒ sin esto, <c>ChannelNotConfigured</c> en vez de <c>Sent</c>.</description></item>
/// <item><description><c>EmailSettings.Host</c> fijado explícitamente a vacío en la MISMA instancia
/// — aunque ya no gobierna <c>useConsoleEmailSender</c> (ver siguiente punto), se mantiene
/// consistente con un remitente de prueba real.</description></item>
/// <item><description><c>EmailTransportDescriptor</c> — registrado aparte como singleton de
/// prueba: es el booleano que declara si el transporte activo es consola (<c>IsConsoleTransport</c>
/// en la respuesta), calculado en <c>Program.cs</c> con la MISMA condición de registro que
/// <c>EmailSettings</c> (arranque, no en tiempo de resolución) — fijarlo aquí es la única forma de
/// que no dependa de si la máquina tiene <c>appsettings.Development.json</c> con host SMTP
/// poblado.</description></item>
/// <item><description>Remitente del canal Renting — NO se puede fijar por configuración: cuando el
/// canal está deshabilitado (como en CI), <c>AddRentingChannel</c> hace un retorno temprano con
/// <c>services.Configure&lt;RentingChannelOptions&gt;(o =&gt; o.Enabled = false)</c> SIN poblar
/// ningún otro campo — cualquier valor puesto en la configuración de entrada queda pisado. Por eso
/// se llama <c>services.Configure&lt;RentingChannelOptions&gt;</c> aquí, DESPUÉS del registro de la
/// app, para fijar el remitente de prueba sin tocar <c>Enabled</c> —
/// <see cref="IExplicitChannelEmailSender.IsChannelAvailable"/> ya está sustituido arriba, así que
/// ese remitente solo importa para <c>ResolveSender(TenantApi)</c> cuando el test simula el canal
/// disponible.</description></item>
/// </list>
/// Valores de prueba obviamente falsos (dominio <c>flit.test</c>) — nunca remitentes reales de FLIT
/// ni de Renting.
/// </remarks>
public sealed class EnviosTestFactory : WebApplicationFactory<Program>
{
    private const string TestSmtpSenderEmail = "pruebas-smtp@flit.test";
    private const string TestSmtpSenderName = "FLIT Pruebas (SMTP)";
    private const string TestRentingSenderEmail = "pruebas-renting@flit.test";
    private const string TestRentingSenderName = "FLIT Pruebas (Renting)";

    // Internal, no public: IExplicitChannelEmailSender es internal en Flit.Infrastructure
    // (InternalsVisibleTo cubre Flit.Admin.Tests) — una propiedad pública no puede exponer un tipo
    // menos accesible (CS0053).
    internal IExplicitChannelEmailSender ExplicitChannelEmailSender { get; } = Substitute.For<IExplicitChannelEmailSender>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped(_ => ExplicitChannelEmailSender);

            // Remitente y transporte SMTP deterministas — EmailSettings se registra en Program.cs
            // como instancia YA MATERIALIZADA (AddSingleton de un objeto, no de un tipo resuelto en
            // caliente); solo sobrescribiendo la registración del servicio en sí (aquí, al final del
            // pipeline) se logra que el test la vea. EmailTransportDescriptor viaja aparte por la
            // misma razón: decide IsConsoleTransport y también se calcula en el arranque.
            services.AddSingleton(new EmailSettings
            {
                DefaultSenderEmail = TestSmtpSenderEmail,
                DefaultSenderName = TestSmtpSenderName,
                Host = string.Empty,
            });
            services.AddSingleton(new EmailTransportDescriptor(IsConsole: false));

            // Remitente del canal Renting: DESPUÉS del registro de la app (ConfigureTestServices se
            // aplica al final), porque AddRentingChannel con el canal deshabilitado sobre-escribe
            // RentingChannelOptions entero salvo Enabled — poner esto en ConfigureAppConfiguration no
            // sobreviviría a ese retorno temprano.
            services.Configure<RentingChannelOptions>(o =>
            {
                o.SendEmailSenderEmail = TestRentingSenderEmail;
                o.SendEmailSenderUsername = TestRentingSenderName;
            });
        });
    }
}
