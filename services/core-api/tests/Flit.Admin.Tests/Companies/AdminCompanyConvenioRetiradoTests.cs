using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// HU #14358 AC1 — las rutas del convenio de firma ya no existen: responden 404 también al Super Admin, y como
/// ningún endpoint las atiende, no se crea ni se modifica ningún convenio.
/// <para>Uso de ejemplo: <c>PUT /api/v1/admin/companies/{tenantId}/transit-agreements/{oficina}</c> → 404.</para>
/// </summary>
public sealed class AdminCompanyConvenioRetiradoTests
    : IClassFixture<AdminCompanyReservedSectionsTests.HierarchyFactory>
{
    private static readonly Guid Compania = Guid.Parse("e0000000-0000-4000-8000-00000000014a");
    private static readonly Guid Oficina = Guid.Parse("e0000000-0000-4000-8000-0000000014f1");

    private readonly AdminCompanyReservedSectionsTests.HierarchyFactory _factory;

    public AdminCompanyConvenioRetiradoTests(AdminCompanyReservedSectionsTests.HierarchyFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task AC1_GET_de_convenios_como_SuperAdmin_responde_404()
    {
        var response = await SendAsync("GET", $"/api/v1/admin/companies/{Compania}/transit-agreements");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC1_PUT_de_convenio_como_SuperAdmin_responde_404()
    {
        var response = await SendAsync("PUT", $"/api/v1/admin/companies/{Compania}/transit-agreements/{Oficina}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void AC1_Ningun_endpoint_atiende_el_convenio_y_el_historial_de_auditoria_se_conserva()
    {
        _ = _factory.CreateClient();
        var rutas = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .ToList();

        rutas.Should().NotContain(r => r.Contains("transit-agreements", StringComparison.OrdinalIgnoreCase));
        rutas.Should().Contain(r => r.EndsWith("/audit-log", StringComparison.Ordinal));
    }

    private async Task<HttpResponseMessage> SendAsync(string method, string url)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "PUT")
            request.Content = new StringContent("{\"active\":true}", System.Text.Encoding.UTF8, "application/json");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
