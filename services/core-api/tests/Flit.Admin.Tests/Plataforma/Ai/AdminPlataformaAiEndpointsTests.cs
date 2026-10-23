using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Ai;

/// <summary>HU #13509 / #13512 — borde HTTP authN/authZ de <c>/api/v1/admin/plataforma/ai/*</c>.</summary>
public sealed class AdminPlataformaAiEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ToolsUrl = "/api/v1/admin/plataforma/ai/tools";
    private const string TransitionsUrl = "/api/v1/admin/plataforma/ai/tools/ocr/transitions";
    private const string AlertsUrl = "/api/v1/admin/plataforma/ai/alerts?acked=false";
    private static readonly string AckUrl = $"/api/v1/admin/plataforma/ai/alerts/{Guid.CreateVersion7()}/ack";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminPlataformaAiEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Theory]
    [InlineData(ToolsUrl)]
    [InlineData(TransitionsUrl)]
    [InlineData(AlertsUrl)]
    public async Task Get_sin_token_devuelve_401(string url)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_ack_sin_token_devuelve_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(AckUrl, content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("OperadorCustom")]
    public async Task Get_tools_con_usuario_no_superadmin_devuelve_403(string rol)
    {
        var tenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(tenantId, rol));

        var response = await client.GetAsync(ToolsUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("OperadorCustom")]
    public async Task Get_transitions_con_usuario_no_superadmin_devuelve_403(string rol)
    {
        var tenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(tenantId, rol));

        var response = await client.GetAsync(TransitionsUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("OperadorCustom")]
    public async Task Get_alerts_con_usuario_no_superadmin_devuelve_403(string rol)
    {
        var tenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(tenantId, rol));

        var response = await client.GetAsync(AlertsUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("OperadorCustom")]
    public async Task Post_ack_con_usuario_no_superadmin_devuelve_403(string rol)
    {
        var tenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");
        var client = ClientWith(TestTokenFactory.CreateOtAdminToken(tenantId, rol));

        var response = await client.PostAsync(AckUrl, content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(ToolsUrl)]
    [InlineData(TransitionsUrl)]
    [InlineData(AlertsUrl)]
    public async Task Get_con_superadmin_pasa_el_borde_de_autorizacion(string url)
    {
        var client = ClientWith(TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_ack_con_superadmin_pasa_el_borde_de_autorizacion()
    {
        var client = ClientWith(TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.PostAsync(AckUrl, content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
