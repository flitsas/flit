using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitGrants;

/// <summary>
/// Contrato de autorización de la API de organismos de tránsito y audit log
/// (HU #10192): el catálogo (AC1), los grants (AC2/AC3/AC5) y el historial (AC4)
/// exigen rol SuperAdmin — sin token → 401; con rol no SuperAdmin → 403. La
/// autorización corta la petición antes de tocar la base de datos.
///
/// Uso de ejemplo:
/// <code>
/// var client = factory.CreateClient();
/// var response = await client.GetAsync("/api/v1/admin/transit-offices");
/// </code>
/// </summary>
public sealed class AdminTransitGrantsAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TenantId = "22222222-2222-2222-2222-222222222222";
    private const string OfficeId = "aaaaaaaa-0001-4000-8000-000000000001";

    private static readonly string CatalogUrl = "/api/v1/admin/transit-offices";
    private static readonly string GrantsUrl = $"/api/v1/admin/companies/{TenantId}/transit-grants";
    private static readonly string GrantUrl = $"/api/v1/admin/companies/{TenantId}/transit-grants/{OfficeId}";
    private static readonly string AuditLogUrl = $"/api/v1/admin/companies/{TenantId}/audit-log";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminTransitGrantsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Catalog_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync(CatalogUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Catalog_WithNonSuperAdmin_Returns403()
    {
        var response = await Operador().GetAsync(CatalogUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostGrant_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync(GrantsUrl, new { transitOfficeId = OfficeId });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostGrant_WithNonSuperAdmin_Returns403()
    {
        var response = await Operador()
            .PostAsJsonAsync(GrantsUrl, new { transitOfficeId = OfficeId });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteGrant_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().DeleteAsync(GrantUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetGrants_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync(GrantsUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAuditLog_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync(AuditLogUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAuditLog_WithNonSuperAdmin_Returns403()
    {
        var response = await Operador().GetAsync(AuditLogUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Operador()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));
        return client;
    }
}
