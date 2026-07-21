using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitOffices;

/// <summary>
/// Seguridad de <c>PUT /api/v1/admin/transit-offices/{id}/quipux-settings</c> (HU #10710).
/// La parametrización Quipux del catálogo es exclusiva de SuperAdmin, igual que el estado
/// operativo: ot_admin —que sí puede leer el catálogo— queda fuera (403). La autorización
/// corta la petición antes de tocar la base de datos.
/// </summary>
public sealed class AdminTransitOfficesQuipuxSettingsAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string Url =
        $"/api/v1/admin/transit-offices/{Guid.NewGuid()}/quipux-settings";

    private static readonly object Body = new
    {
        divipoCode = "05001",
        quipuxRegistration = true,
        quipuxTransfer = false,
        quipuxOther = false,
    };

    private readonly WebApplicationFactory<Program> _factory;

    public AdminTransitOfficesQuipuxSettingsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(Url, Body, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithNonAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PutAsJsonAsync(Url, Body, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithOtAdminRole_Returns403()
    {
        // ot_admin ve el catálogo (OtModulePolicy) pero NO parametriza la radicación Quipux.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("ot_admin"));

        var response = await client.PutAsJsonAsync(Url, Body, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithSuperAdminRole_IsAuthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.PutAsJsonAsync(Url, Body, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
