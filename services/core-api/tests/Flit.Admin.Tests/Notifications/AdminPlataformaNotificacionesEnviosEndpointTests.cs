using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
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
        _factory.EmailSender.ClearReceivedCalls();
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
        _factory.EmailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
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

        await _factory.EmailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
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
        _factory.EmailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var client = SuperAdminClient();

        var first = await client.PostAsJsonAsync(
            EnviosUrl, new { templateId = "security.invitation", channel = "FLIT_SMTP" },
            TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.EmailSender.ClearReceivedCalls();
        var second = await client.PostAsJsonAsync(
            EnviosUrl, new { templateId = "analytics.alert", channel = "FLIT_SMTP" },
            TestContext.Current.CancellationToken);

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await second.Content.ReadFromJsonAsync<RateLimitedDto>(TestContext.Current.CancellationToken);
        body!.Error.Should().Be("limite_frecuencia");
        body.RetryAfterSeconds.Should().BeGreaterThan(0);

        await _factory.EmailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_ConCanalTenantApi_Returns400ConCausaConfiguracionIncompleta()
    {
        SetMailbox("pruebas-envio-ac4@flit.co");

        var response = await SuperAdminClient().PostAsJsonAsync(
            EnviosUrl, new { templateId = "security.invitation", channel = "TENANT_API" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorDto>(TestContext.Current.CancellationToken);
        body!.Error.Should().Be("configuracion_incompleta");

        await _factory.EmailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
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
        string? SenderEmail, string? SenderName, DateTimeOffset? SentAt, bool IsConsoleTransport);

    private sealed record ErrorDto(string Error, string? Message);

    private sealed record RateLimitedDto(string Error, string? Message, int? RetryAfterSeconds);
}

/// <summary>
/// Factory que sustituye <see cref="IEmailSender"/> por un doble NSubstitute — mismo patrón que
/// <c>TransferStartEndpointTests.TransferTestFactory</c> — para que esta suite nunca intente una
/// conexión SMTP real (appsettings.Development.json trae host real).
/// </summary>
public sealed class EnviosTestFactory : WebApplicationFactory<Program>
{
    public IEmailSender EmailSender { get; } = Substitute.For<IEmailSender>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped(_ => EmailSender);
        });
    }
}
