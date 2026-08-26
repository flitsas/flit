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

        // HU #11371 — IsConfigured sigue la MISMA regla de disponibilidad que decide el envío de
        // prueba (adaptador Renting registrado en este ambiente), NO si el remitente está poblado.
        // Deliberadamente no se asume aquí si el canal Renting está habilitado o no en este
        // ambiente de pruebas (puede variar por máquina/CI vía RENTING_API_ENABLED) — lo único que
        // se afirma es que ambas señales SIEMPRE coinciden.
        using var scope = _factory.Services.CreateScope();
        var rentingAdapterRegistered =
            scope.ServiceProvider.GetService<IRentingEmailApiSender>() is not null;
        tenantApi.IsConfigured.Should().Be(
            rentingAdapterRegistered,
            "IsConfigured refleja si el adaptador Renting está registrado en este ambiente, la MISMA regla que el envío de prueba");

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
