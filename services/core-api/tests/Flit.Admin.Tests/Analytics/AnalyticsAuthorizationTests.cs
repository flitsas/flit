using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Analytics;

/// <summary>
/// Autorización de <c>/api/v1/analytics/*</c> (corrección "dashboard en 0 para roles
/// personalizados"): las lecturas (overview, monthly-trend, productivity/top,
/// procedures) deben estar disponibles para cualquier usuario autenticado del tenant,
/// sin exigir el rol AdminCompany/SuperAdmin; la exportación (Excel/PDF) sigue
/// restringida a AdminCompanyPolicy.
/// </summary>
public sealed class AnalyticsAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string OverviewUrl = "/api/v1/analytics/overview?from=2026-06-01&to=2026-06-30";
    private const string MonthlyTrendUrl = "/api/v1/analytics/monthly-trend?from=2026-01-01&to=2026-06-30";
    private const string TopProducersUrl = "/api/v1/analytics/productivity/top?from=2026-06-01&to=2026-06-30";
    private const string ProceduresUrl = "/api/v1/analytics/procedures?from=2026-06-01&to=2026-06-30";
    private const string ExportExcelUrl = "/api/v1/analytics/export/excel?from=2026-06-01&to=2026-06-30";
    private const string ExportPdfUrl = "/api/v1/analytics/export/executive-pdf";

    private readonly WebApplicationFactory<Program> _factory;

    public AnalyticsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(OverviewUrl)]
    [InlineData(MonthlyTrendUrl)]
    [InlineData(TopProducersUrl)]
    [InlineData(ProceduresUrl)]
    [InlineData(ExportExcelUrl)]
    public async Task WithoutToken_Returns401(string url)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExportExecutivePdf_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            ExportPdfUrl,
            new { from = "2026-06-01", to = "2026-06-30" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(OverviewUrl)]
    [InlineData(MonthlyTrendUrl)]
    [InlineData(TopProducersUrl)]
    [InlineData(ProceduresUrl)]
    public async Task WithCustomRole_ReadEndpoints_AreNoLongerForbidden(string url)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "OperadorCustom"));

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithCustomRole_ExportExcel_StillReturns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "OperadorCustom"));

        var response = await client.GetAsync(ExportExcelUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithCustomRole_ExportExecutivePdf_StillReturns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "OperadorCustom"));

        var response = await client.PostAsJsonAsync(
            ExportPdfUrl,
            new { from = "2026-06-01", to = "2026-06-30", tenantId = TenantId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithAdminCompanyRole_ExportExcel_IsAuthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "AdminCompany"));

        var response = await client.GetAsync(ExportExcelUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithCustomRole_MonthlyTrendForAnotherTenant_StillReturns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "OperadorCustom"));

        var response = await client.GetAsync(
            $"/api/v1/analytics/monthly-trend?from=2026-01-01&to=2026-06-30&tenantId={OtherTenantId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("No está autorizado para consultar métricas de otro tenant.");
    }
}
