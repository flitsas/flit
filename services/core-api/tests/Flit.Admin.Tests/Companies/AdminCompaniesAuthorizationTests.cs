using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// Tests de seguridad del endpoint <c>GET /api/v1/admin/companies/index</c>
/// (HU #10189) — AC3 (rol no SuperAdmin → 403) y AC4 (sin token → 401).
/// La autorización corta la petición antes de tocar la base de datos.
/// </summary>
public sealed class AdminCompaniesAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string IndexUrl = "/api/v1/admin/companies/index";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminCompaniesAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AC4_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC3_WithNonSuperAdminRole_Returns403WithMessage()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Error.Should().Be("Acceso restringido: se requiere rol SuperAdmin");
    }

    private sealed record ErrorBody(string Error);
}
