using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// Tests de seguridad del endpoint <c>PUT /api/v1/admin/companies/{tenantId}/status</c>
/// (#10118): sin token → 401; rol no SuperAdmin → 403. La autorización corta la petición
/// antes de tocar la base de datos.
/// </summary>
public sealed class AdminCompanyStatusAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string StatusUrl =
        $"/api/v1/admin/companies/{Guid.NewGuid()}/status";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminCompanyStatusAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(StatusUrl, new { estadoActivo = false });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PutAsJsonAsync(StatusUrl, new { estadoActivo = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
