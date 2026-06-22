using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies.Settings;

/// <summary>
/// AC5 (RF01) — el GET y el PUT de <c>/api/v1/admin/companies/{tenantId}/settings</c>
/// exigen rol SuperAdmin: sin token → 401; con rol no SuperAdmin → 403. La
/// autorización corta la petición antes de tocar la base de datos.
/// </summary>
public sealed class AdminCompaniesSettingsAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SettingsUrl =
        "/api/v1/admin/companies/22222222-2222-2222-2222-222222222222/settings";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminCompaniesSettingsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AC5_Get_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(SettingsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC5_Put_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(SettingsUrl, new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC5_Get_WithNonSuperAdminRole_Returns403WithMessage()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(SettingsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.Error.Should().Be("Acceso restringido: se requiere rol SuperAdmin");
    }

    [Fact]
    public async Task AC5_Put_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PutAsJsonAsync(SettingsUrl, new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ErrorBody(string Error);
}
