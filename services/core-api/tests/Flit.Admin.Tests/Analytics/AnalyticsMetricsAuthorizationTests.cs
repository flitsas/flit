using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Analytics;

/// <summary>
/// Autorización de los endpoints de métricas Reportes 2.0 (HU-B):
/// <c>/api/v1/analytics/{ot-metrics,funnel,usage,live-overview}</c>.
/// Réplica del contrato de <see cref="AnalyticsAuthorizationTests"/> con la diferencia §4 del
/// contrato: NO hay vista global — el SuperAdmin sin <c>tenantId</c> recibe 400 (no 200 global).
/// Estas rutas se cortan ANTES de tocar la base de datos (401/403/400), por lo que no requieren
/// PostgreSQL.
/// </summary>
public sealed class AnalyticsMetricsAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string OtMetricsUrl = "/api/v1/analytics/ot-metrics?from=2026-06-01&to=2026-06-30";
    private const string FunnelUrl = "/api/v1/analytics/funnel?from=2026-06-01&to=2026-06-30";
    private const string UsageUrl = "/api/v1/analytics/usage?from=2026-06-01&to=2026-06-30";
    private const string LiveOverviewUrl = "/api/v1/analytics/live-overview";

    private readonly WebApplicationFactory<Program> _factory;

    public AnalyticsMetricsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory] // Grupo con RequireAuthorization: sin token → 401 en los 4 endpoints
    [InlineData(OtMetricsUrl)]
    [InlineData(FunnelUrl)]
    [InlineData(UsageUrl)]
    [InlineData(LiveOverviewUrl)]
    public async Task WithoutToken_Returns401(string url)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory] // Tenant admin pidiendo el tenantId de OTRA compañía → 403 con detalle en español
    [InlineData(OtMetricsUrl)]
    [InlineData(FunnelUrl)]
    [InlineData(UsageUrl)]
    [InlineData(LiveOverviewUrl)]
    public async Task WithTenantToken_RequestingAnotherTenant_Returns403(string url)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var separator = url.Contains('?') ? "&" : "?";
        var response = await client.GetAsync(
            $"{url}{separator}tenantId={OtherTenantId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("No está autorizado para consultar métricas de otro tenant.");
    }

    [Theory] // §4: SIN vista global — SuperAdmin sin tenantId → 400 descriptivo (no 200 global)
    [InlineData(OtMetricsUrl)]
    [InlineData(FunnelUrl)]
    [InlineData(UsageUrl)]
    [InlineData(LiveOverviewUrl)]
    public async Task WithSuperAdminToken_WithoutTenantId_Returns400(string url)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "SuperAdmin"));

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("el SuperAdmin debe indicar la compañía");
    }
}
