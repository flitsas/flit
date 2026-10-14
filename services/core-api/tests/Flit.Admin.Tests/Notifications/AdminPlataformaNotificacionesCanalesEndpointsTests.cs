using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Admin;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Notifications;

/// <summary>
/// HU #11367 (Feature #11349) — <c>GET /api/v1/admin/plataforma/notificaciones/canales</c>: los
/// dos canales de notificación con su remitente (HU #13359: el que informa core-notificaciones; aquí, un doble). Mismo patrón que
/// <see cref="AdminPlataformaNotificacionesEndpointsTests"/> (HU #11366): host real vía
/// <c>WebApplicationFactory&lt;Program&gt;</c>, sin necesidad de PostgreSQL (endpoint de solo
/// lectura de configuración, no toca <c>FlitDbContext</c>).
/// </summary>
public sealed class AdminPlataformaNotificacionesCanalesEndpointsTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string CanalesUrl = "/api/v1/admin/plataforma/notificaciones/canales";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminPlataformaNotificacionesCanalesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ── Autorización — mismo guardián SuperAdmin que el resto del módulo ────────────

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(CanalesUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_WithNonSuperAdminRole_Returns403()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(CanalesUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── AC1 — dos canales, FLIT_SMTP es el default con su remitente (HU #13359: el que informa Notificaciones) ──

    [Fact]
    public async Task AC1_Get_AsSuperAdmin_Returns200WithTwoChannelsAndFlitSmtpAsDefault()
    {
        using var client = SuperAdminClient(new CanalesFalsos(rentingDisponible: false));

        var response = await client.GetAsync(CanalesUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChannelsDto>(TestContext.Current.CancellationToken);
        body!.Channels.Select(c => c.Channel).Should().BeEquivalentTo(["FLIT_SMTP", "TENANT_API"]);
        var flitSmtp = body.Channels.Single(c => c.Channel == "FLIT_SMTP");
        flitSmtp.IsDefault.Should().BeTrue();
        flitSmtp.IsConfigured.Should().BeTrue();
        flitSmtp.SenderEmail.Should().Be("pruebas-smtp@flit.test");
        body.Channels.Single(c => c.Channel == "TENANT_API").IsDefault.Should().BeFalse();
    }

    // ── AC2/AC3 + HU #11371 — el canal del cliente: su propio remitente y su disponibilidad real ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AC2_AC3_TenantApi_TraeSuRemitente_YEstaConfiguradoSoloSiNotificacionesLoPuedeUsar(bool disponible)
    {
        using var client = SuperAdminClient(new CanalesFalsos(disponible));

        var response = await client.GetAsync(CanalesUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChannelsDto>(TestContext.Current.CancellationToken);
        var tenantApi = body!.Channels.Single(c => c.Channel == "TENANT_API");
        tenantApi.SenderEmail.Should().Be("pruebas-renting@flit.test");
        tenantApi.SenderName.Should().Be("FLIT Pruebas (Renting)");
        tenantApi.IsConfigured.Should().Be(disponible, "la MISMA regla con la que se puede enviar por el canal");
    }

    private HttpClient SuperAdminClient(ICanalesDeNotificaciones? canales = null)
    {
        var factory = canales is null
            ? _factory
            : _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => canales)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        return client;
    }

    private sealed class CanalesFalsos(bool rentingDisponible) : ICanalesDeNotificaciones
    {
        public Task<IReadOnlyList<CanalDeNotificacion>> ListarAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CanalDeNotificacion>>(
            [
                new(NotificationChannel.FlitSmtp, true, "pruebas-smtp@flit.test", "FLIT Pruebas (SMTP)", Consola: false),
                new(NotificationChannel.TenantApi, rentingDisponible, "pruebas-renting@flit.test", "FLIT Pruebas (Renting)", Consola: false),
            ]);

        public Task<EmailSendResult> EnviarPruebaAsync(NotificationChannel canal, EmailMessage mensaje, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed record ChannelsDto(List<ChannelDto> Channels);

    private sealed record ChannelDto(
        string Channel,
        string Label,
        bool IsDefault,
        bool IsConfigured,
        string? SenderEmail,
        string? SenderName);
}
