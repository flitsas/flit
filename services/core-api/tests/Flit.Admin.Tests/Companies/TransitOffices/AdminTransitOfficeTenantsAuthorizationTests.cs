using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitOffices;

/// <summary>
/// Tests de seguridad de <c>/api/v1/admin/transit-office-tenants/*</c> (refactor
/// adminOT) — exigen rol SuperAdmin (a diferencia de <c>/api/v1/admin/ot/*</c>, que
/// también permite <c>ot_admin</c>). La autorización corta la petición antes de tocar
/// la base de datos.
/// </summary>
public sealed class AdminTransitOfficeTenantsAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string IndexUrl = "/api/v1/admin/transit-office-tenants/index";
    private const string CreateUrl = "/api/v1/admin/transit-office-tenants";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminTransitOfficeTenantsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Index_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Index_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Index_WithOtAdminRole_Returns403()
    {
        // El alta/listado de tenants OT es exclusiva de SuperAdmin — a diferencia de
        // /api/v1/admin/ot/*, ot_admin NO tiene acceso a este grupo.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("ot_admin"));

        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(CreateUrl, new { }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PostAsJsonAsync(CreateUrl, new { }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
