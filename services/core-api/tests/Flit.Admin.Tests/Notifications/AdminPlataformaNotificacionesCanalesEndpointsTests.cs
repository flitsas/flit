using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.Renting;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Admin.Tests.Notifications;

/// <summary>
/// HU #11367 (Feature #11349) — <c>GET /api/v1/admin/plataforma/notificaciones/canales</c>: los
/// dos canales de notificación con su remitente resuelto por configuración. Mismo patrón que
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

    // ── AC1 — dos canales, FLIT_SMTP es el default con el remitente de la config SMTP ──────

    [Fact]
    public async Task AC1_Get_AsSuperAdmin_Returns200WithTwoChannelsAndFlitSmtpAsDefault()
    {
        using var client = SuperAdminClient();

        var response = await client.GetAsync(CanalesUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChannelsDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Channels.Should().HaveCount(2);
        body.Channels.Select(c => c.Channel).Should().BeEquivalentTo(["FLIT_SMTP", "TENANT_API"]);

        var emailSettings = _factory.Services.GetRequiredService<EmailSettings>();
        var flitSmtp = body.Channels.Single(c => c.Channel == "FLIT_SMTP");
        flitSmtp.IsDefault.Should().BeTrue();
        flitSmtp.SenderEmail.Should().Be(string.IsNullOrWhiteSpace(emailSettings.DefaultSenderEmail)
            ? null
            : emailSettings.DefaultSenderEmail);

        var tenantApi = body.Channels.Single(c => c.Channel == "TENANT_API");
        tenantApi.IsDefault.Should().BeFalse();
    }

    // ── AC2/AC3 — el canal del cliente refleja fielmente su propia configuración ────────────

    [Fact]
    public async Task AC2_AC3_Get_AsSuperAdmin_TenantApiChannelMatchesRentingChannelOptions()
    {
        using var client = SuperAdminClient();

        var response = await client.GetAsync(CanalesUrl, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ChannelsDto>(TestContext.Current.CancellationToken);
        var tenantApi = body!.Channels.Single(c => c.Channel == "TENANT_API");

        var rentingOptions = _factory.Services.GetRequiredService<IOptions<RentingChannelOptions>>().Value;
        var expectedEmail = string.IsNullOrWhiteSpace(rentingOptions.SendEmailSenderEmail)
            ? null
            : rentingOptions.SendEmailSenderEmail;
        var expectedName = string.IsNullOrWhiteSpace(rentingOptions.SendEmailSenderUsername)
            ? null
            : rentingOptions.SendEmailSenderUsername;

        tenantApi.SenderEmail.Should().Be(expectedEmail);
        tenantApi.SenderName.Should().Be(expectedName);
        tenantApi.IsConfigured.Should().Be(expectedEmail is not null && expectedName is not null);

        // AC3 en este entorno de pruebas: sin las variables RENTING_API_SEND_EMAIL_SENDER_*
        // definidas, el canal debe volver "sin configurar" — nunca un error (sigue siendo 200).
        if (expectedEmail is null)
        {
            tenantApi.IsConfigured.Should().BeFalse();
        }
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient SuperAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        return client;
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
