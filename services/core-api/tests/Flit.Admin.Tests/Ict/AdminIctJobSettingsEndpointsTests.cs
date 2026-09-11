using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Ict;

/// <summary>
/// HU #12512 AC3 — borde HTTP de <c>/api/v1/admin/ict/job-settings</c>.
/// Sin Postgres: solo authN/authZ (mismo criterio que IctReportsEndpointsTests).
/// </summary>
public sealed class AdminIctJobSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/v1/admin/ict/job-settings";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminIctJobSettingsEndpointsTests(WebApplicationFactory<Program> factory)
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
    public async Task Get_sin_token_devuelve_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("OperadorCustom")]
    public async Task Get_con_usuario_no_superadmin_devuelve_403(string rol)
    {
        var tenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(tenantId, rol));

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    public async Task Put_con_usuario_no_superadmin_devuelve_403(string rol)
    {
        var tenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(tenantId, rol));

        var response = await client.PutAsync(
            Url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_con_superadmin_pasa_el_borde_de_autorizacion()
    {
        var client = ClientWith(TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
