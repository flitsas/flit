using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Notifications;

/// <summary>
/// HU #11366 (Feature #11349) — grupo de rutas SuperAdmin y buzón de pruebas de notificaciones.
/// Cubre los 5 AC contra <c>WebApplicationFactory&lt;Program&gt;</c> + <c>FlitDbContext</c> real,
/// mismo patrón que <see cref="Flit.Admin.Tests.Security.AuthLoginAndMeEndpointsTests"/> y
/// <see cref="Flit.Admin.Tests.Improntas.AdminImprontasAuthorizationTests"/>.
/// </summary>
/// <remarks>
/// La fila <c>admin.notification_test_settings</c> es única y global de plataforma (HU #11365):
/// no hay <c>tenant_id</c> que aísle un fixture por test, así que el constructor la resetea a
/// "sin configurar" y el <see cref="Dispose"/> restaura el valor previo — igual estrategia de
/// housekeeping best-effort que usan los tests que comparten la BD de desarrollo.
/// </remarks>
public sealed class AdminPlataformaNotificacionesEndpointsTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string BuzonUrl = "/api/v1/admin/plataforma/notificaciones/buzon-pruebas";
    private const string GroupPrefix = "/api/v1/admin/plataforma/notificaciones";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string? _originalEmail;

    public AdminPlataformaNotificacionesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = db.NotificationTestSettings.SingleOrDefault();
        if (row is null)
        {
            row = new NotificationTestSettingsRow { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
            db.NotificationTestSettings.Add(row);
        }

        _originalEmail = row.TestRecipientEmail;
        row.TestRecipientEmail = null;
        row.UpdatedAt = null;
        row.UpdatedBy = null;
        db.SaveChanges();
    }

    // ── AC1 — sin rol SuperAdmin no hay acceso ──────────────────────────────

    [Fact]
    public async Task AC1_Get_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync(BuzonUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC1_Put_WithoutToken_Returns401()
    {
        var response = await _client.PutAsJsonAsync(
            BuzonUrl, new { email = "pruebas@flit.co" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC1_Get_WithNonSuperAdminRole_Returns403AndDoesNotExposeMailbox()
    {
        // Buzón configurado con un valor conocido para probar que el 403 no lo filtra.
        await SetMailboxAsSuperAdminAsync("secreto-ac1@flit.co");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(BuzonUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain("secreto-ac1@flit.co");
    }

    [Fact]
    public async Task AC1_Get_WithOtAdminRole_Returns403()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("ot_admin"));

        var response = await client.GetAsync(BuzonUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC1_Put_WithNonSuperAdminRole_Returns403()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("AdminCompany"));

        var response = await client.PutAsJsonAsync(
            BuzonUrl, new { email = "otro@flit.co" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── AC2 — consulta del buzón sin configurar ─────────────────────────────

    [Fact]
    public async Task AC2_Get_WhenNotConfigured_Returns200WithEmptyMailboxAndNotConfiguredFlag()
    {
        using var client = SuperAdminClient();

        var response = await client.GetAsync(BuzonUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MailboxDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.IsConfigured.Should().BeFalse();
        body.TestRecipientEmail.Should().BeNull();
    }

    // ── AC3 — actualización auditada ────────────────────────────────────────

    [Fact]
    public async Task AC3_Put_WithValidEmail_Returns200PersistsRowAndRecordsActor()
    {
        using var client = SuperAdminClient();
        const string email = "banco-pruebas-ac3@flit.co";

        var response = await client.PutAsJsonAsync(BuzonUrl, new { email }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MailboxDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.IsConfigured.Should().BeTrue();
        body.TestRecipientEmail.Should().Be(email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = await db.NotificationTestSettings.SingleAsync(TestContext.Current.CancellationToken);
        row.TestRecipientEmail.Should().Be(email);
        // Mismo `sub` fijo que TestTokenFactory.CreateToken emite para todos los roles de prueba.
        row.UpdatedBy.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    // ── AC4 — dirección inválida ─────────────────────────────────────────────

    [Fact]
    public async Task AC4_Put_WithInvalidEmail_Returns400AndKeepsPreviousValue()
    {
        using var client = SuperAdminClient();
        const string previous = "valor-previo-ac4@flit.co";
        await SetMailboxAsSuperAdminAsync(previous);

        var response = await client.PutAsJsonAsync(
            BuzonUrl, new { email = "no-es-un-correo" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorBody = await response.Content.ReadFromJsonAsync<ErrorDto>(TestContext.Current.CancellationToken);
        errorBody.Should().NotBeNull();
        errorBody!.Error.Should().Be("correo_invalido");

        var getResponse = await client.GetAsync(BuzonUrl, TestContext.Current.CancellationToken);
        var getBody = await getResponse.Content.ReadFromJsonAsync<MailboxDto>(TestContext.Current.CancellationToken);
        getBody!.TestRecipientEmail.Should().Be(previous);
    }

    // ── AC5 — la pantalla nunca escribe política de tenant ──────────────────

    [Fact]
    public void AC5_RegisteredRoutes_NeverCarryTenantSegmentsAndOnlyGetIsSafeByConstruction()
    {
        var dataSources = _factory.Services.GetServices<EndpointDataSource>();
        var groupEndpoints = dataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw
                && raw.StartsWith(GroupPrefix, StringComparison.Ordinal))
            .ToList();

        groupEndpoints.Should().NotBeEmpty("el grupo de notificaciones debe registrar al menos un endpoint");

        var tenantParameterNames = new[] { "tenant", "office", "ot" };

        foreach (var endpoint in groupEndpoints)
        {
            var tenantParams = endpoint.RoutePattern.Parameters
                .Where(p => tenantParameterNames.Any(t =>
                    p.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            tenantParams.Should().BeEmpty(
                $"la ruta '{endpoint.RoutePattern.RawText}' no debe llevar segmento de tenant/organismo (AC5)");

            var httpMethods = endpoint.Metadata
                .OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
                .SelectMany(m => m.HttpMethods)
                .ToList();

            var isWrite = httpMethods.Any(m => !string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase));
            if (isWrite)
            {
                tenantParams.Should().BeEmpty(
                    $"la ruta de escritura '{endpoint.RoutePattern.RawText}' ({string.Join(",", httpMethods)}) " +
                    "no puede escribir sobre una ruta de tenant (AC5)");
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient SuperAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        return client;
    }

    private async Task SetMailboxAsSuperAdminAsync(string email)
    {
        using var client = SuperAdminClient();
        var response = await client.PutAsJsonAsync(
            BuzonUrl, new { email }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public void Dispose()
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            var row = db.NotificationTestSettings.SingleOrDefault();
            if (row is not null)
            {
                row.TestRecipientEmail = _originalEmail;
                row.UpdatedAt = _originalEmail is null ? null : DateTimeOffset.UtcNow;
                db.SaveChanges();
            }
        }
        catch
        {
            // Housekeeping best-effort: la BD de desarrollo es compartida, un fallo aquí no
            // debe tumbar el resultado del test que ya corrió.
        }
    }

    private sealed record MailboxDto(bool IsConfigured, string? TestRecipientEmail, DateTimeOffset? LastTestSentAt, long RowVersion);

    private sealed record ErrorDto(string Error);
}
