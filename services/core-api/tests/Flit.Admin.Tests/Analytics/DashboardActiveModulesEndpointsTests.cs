using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Analytics;

/// <summary>
/// Autorización de GET /api/v1/analytics/active-modules (HU #12251, Feature #12249). AC2 (401
/// sin token), AC1 (no exige AdminCompanyPolicy: un rol no-admin no debe recibir 401/403) y AC4
/// (aislamiento por tenant: SuperAdmin con tenantId explícito no es bloqueado, sin tenantId no
/// hay vista global → 400, y un rol no-admin pidiendo OTRO tenant → 403). Mismo patrón que
/// <see cref="AnalyticsAuthorizationTests"/>: los tests de integración de este ensamblado NO
/// requieren un Postgres migrado (<c>TestEnvironment.DisableAutoMigrate</c>), así que solo
/// verifican el resultado de autorización/enrutamiento (401/403/400), no el payload 200
/// completo — ese comportamiento (AC1 exactamente 3 flags, AC3 defaults sin fila) lo cubre
/// <c>GetActiveModulesHandlerTests</c> contra EF InMemory.
/// </summary>
public sealed class DashboardActiveModulesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherTenantId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private const string Url = "/api/v1/analytics/active-modules";

    private readonly WebApplicationFactory<Program> _factory;

    public DashboardActiveModulesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AC2_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC1_WithNonAdminRole_IsNotForbidden()
    {
        // No exige AdminAuthorization.AdminCompanyPolicy: un rol no-admin (gestor/radicador/
        // OperadorCustom) no debe recibir 401/403, a diferencia de
        // GET /api/v1/admin/companies/{tenantId}/settings.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "OperadorCustom"));

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC4_SuperAdmin_WithExplicitTenantId_IsNotBlocked()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "SuperAdmin"));

        var response = await client.GetAsync(
            $"{Url}?tenantId={OtherTenantId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AC4_SuperAdmin_WithoutTenantId_Returns400_NoGlobalView()
    {
        // A diferencia de overview/monthly-trend, aquí NO hay vista global: los flags son de
        // UN tenant, así que el SuperAdmin debe indicar tenantId explícitamente.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "SuperAdmin"));

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AC4_NonSuperAdmin_RequestingAnotherTenant_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(TenantId, "OperadorCustom"));

        var response = await client.GetAsync(
            $"{Url}?tenantId={OtherTenantId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
