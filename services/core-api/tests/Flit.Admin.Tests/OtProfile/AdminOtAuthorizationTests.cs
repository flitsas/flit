using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.OtProfile;

/// <summary>
/// Autorización de endpoints OT admin (HU #10215) — exigen SuperAdmin u ot_admin y tenant_id en JWT.
/// </summary>
public sealed class AdminOtAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ProfileUrl = "/api/v1/admin/ot/profile";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminOtAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProfile_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(ProfileUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProfile_WithNonOtModuleRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(ProfileUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.Error.Should().Be("Acceso restringido: se requiere rol SuperAdmin u ot_admin");
    }

    [Fact]
    public async Task GetProfile_WithSuperAdminToken_Returns401WhenMissingTenantClaim()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(ProfileUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProfile_WithSuperAdminAndTenant_IsAuthorized()
    {
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                TestTokenFactory.CreateOtAdminToken(tenantId, "SuperAdmin"));

        var response = await client.GetAsync(ProfileUrl);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProfile_WithOtAdminTokenMissingTenantClaim_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("ot_admin"));

        var response = await client.GetAsync(ProfileUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record ErrorBody(string Error);
}
