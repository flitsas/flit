using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Improntas;

/// <summary>
/// Seguridad de endpoints admin improntas (Feature #10462): <c>GET /api/v1/admin/improntas</c>
/// (HU #10468) y <c>POST /api/v1/admin/improntas/generate</c> (HU #10467). Sin token → 401;
/// rol distinto de SuperAdmin → 403.
/// </summary>
public sealed class AdminImprontasAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string IndexUrl = "/api/v1/admin/improntas";
    private const string GenerateUrl = "/api/v1/admin/improntas/generate";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminImprontasAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static readonly object ValidBody = new
    {
        placa = "ABC123",
        documento = "1234567890",
        orgNombre = "FLIT SAS",
        operador = "Operador X",
    };

    [Fact]
    public async Task AC2_Index_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC2_Index_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC2_Index_WithOtAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("ot_admin"));

        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC2_Generate_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(GenerateUrl, ValidBody, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC2_Generate_WithNonSuperAdminRole_Returns403WithMessage()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PostAsJsonAsync(GenerateUrl, ValidBody, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Error.Should().Be("Acceso restringido: se requiere rol SuperAdmin");
    }

    [Fact]
    public async Task AC2_Generate_WithAdminCompanyRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("AdminCompany"));

        var response = await client.PostAsJsonAsync(GenerateUrl, ValidBody, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ErrorBody(string Error);
}
