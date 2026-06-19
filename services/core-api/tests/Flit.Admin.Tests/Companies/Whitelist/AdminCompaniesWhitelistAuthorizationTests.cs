using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies.Whitelist;

/// <summary>
/// Contrato de autorización de la API de whitelist (HU #10191): el POST (AC4) y el
/// GET (AC6) de <c>/api/v1/admin/companies/{tenantId}/whitelist</c> exigen rol
/// SuperAdmin — sin token → 401; con rol no SuperAdmin → 403. La autorización corta
/// la petición antes de tocar la base de datos.
/// </summary>
public sealed class AdminCompaniesWhitelistAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string WhitelistUrl =
        "/api/v1/admin/companies/22222222-2222-2222-2222-222222222222/whitelist";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminCompaniesWhitelistAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(WhitelistUrl, new { emails = new[] { "a@co.com" } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(WhitelistUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PostAsJsonAsync(WhitelistUrl, new { emails = new[] { "a@co.com" } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
