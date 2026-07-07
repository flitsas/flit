using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitOffices;

/// <summary>
/// Seguridad de <c>GET /api/v1/admin/transit-offices/operational-status</c> (RF01). A
/// diferencia del catálogo <c>GET /admin/transit-offices</c> (SuperAdmin u ot_admin), la
/// gestión del ciclo de vida es exclusiva de SuperAdmin: ot_admin queda fuera (403). La
/// autorización corta la petición antes de tocar la base de datos.
/// </summary>
public sealed class AdminTransitOfficesOperationalStatusAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/v1/admin/transit-offices/operational-status";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminTransitOfficesOperationalStatusAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithNonAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithOtAdminRole_Returns403()
    {
        // ot_admin ve el catálogo (OtModulePolicy) pero NO la gestión del ciclo de vida.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("ot_admin"));

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithSuperAdminRole_IsAuthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
