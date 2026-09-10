using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Plataforma;

/// <summary>HU #11701 — autorización y validación del preview sintético del FUR.</summary>
public sealed class AdminPlataformaFurEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string PreviewUrl = "/api/v1/admin/plataforma/fur/preview";
    private const string ClassificationsUrl = "/api/v1/admin/plataforma/fur/classifications";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminPlataformaFurEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Preview_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            PreviewUrl,
            ValidBody(),
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Preview_WithNonSuperAdmin_Returns403()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PostAsJsonAsync(
            PreviewUrl,
            ValidBody(),
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Preview_MissingProcedureTypeId_Returns400()
    {
        using var client = SuperAdminClient();
        var response = await client.PostAsJsonAsync(
            PreviewUrl,
            new { vehicleKind = "carro", sellerPersonKind = "natural", buyerPersonKind = "natural" },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preview_InvalidVehicleKind_Returns400()
    {
        using var client = SuperAdminClient();
        var response = await client.PostAsJsonAsync(
            PreviewUrl,
            new
            {
                procedureTypeId = Guid.NewGuid(),
                vehicleKind = "barco",
                sellerPersonKind = "natural",
                buyerPersonKind = "natural",
            },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Classifications_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(ClassificationsUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient SuperAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        return client;
    }

    private static object ValidBody() => new
    {
        procedureTypeId = Guid.NewGuid(),
        vehicleKind = "carro",
        sellerPersonKind = "natural",
        buyerPersonKind = "natural",
    };
}
