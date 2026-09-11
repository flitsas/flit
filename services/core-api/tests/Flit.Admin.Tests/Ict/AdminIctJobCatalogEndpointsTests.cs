using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Ict;

/// <summary>
/// HU #12513 AC3 — borde HTTP de catálogo y runs. Sin Postgres: solo authN/authZ.
/// </summary>
public sealed class AdminIctJobCatalogEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string CatalogUrl = "/api/v1/admin/ict/jobs";
    private const string RunsUrl = "/api/v1/admin/ict/jobs/orchestrator/runs";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminIctJobCatalogEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Catalogo_sin_token_devuelve_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(CatalogUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("OperadorCustom")]
    public async Task Catalogo_con_usuario_no_superadmin_devuelve_403(string rol)
    {
        var tenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(tenantId, rol));
        var response = await client.GetAsync(CatalogUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    public async Task Runs_con_usuario_no_superadmin_devuelve_403(string rol)
    {
        var tenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(tenantId, rol));
        var response = await client.GetAsync(RunsUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Catalogo_con_superadmin_pasa_el_borde_de_autorizacion()
    {
        var client = ClientWith(TestTokenFactory.CreateToken("SuperAdmin"));
        var response = await client.GetAsync(CatalogUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
