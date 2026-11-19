using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// HU #14410 (Feature #14408) — las rutas de preferencias de documentos, propias y de hija, ya no existen: responden
/// 404 también al Super Admin. Las rutas vecinas siguen publicadas y el Administrador de compañía conserva
/// Representantes legales y Mandatarios. Las demás secciones reservadas las cubre <see cref="AdminCompanyReservedSectionsTests"/>.
/// <para>Uso de ejemplo: <c>PUT /api/v1/admin/companies/{tenantId}/document-params</c> → 404.</para>
/// </summary>
public sealed class AdminCompanyPreferenciasDocumentosRetiradasTests
    : IClassFixture<AdminCompanyReservedSectionsTests.HierarchyFactory>
{
    private static readonly Guid Compania = Guid.Parse("e0000000-0000-4000-8000-00000000014b");
    private static readonly Guid Cabeza = Guid.Parse("e0000000-0000-4000-8000-000000000100");
    private static readonly Guid Hija = Guid.Parse("e0000000-0000-4000-8000-000000001100");

    private readonly AdminCompanyReservedSectionsTests.HierarchyFactory _factory;

    public AdminCompanyPreferenciasDocumentosRetiradasTests(AdminCompanyReservedSectionsTests.HierarchyFactory factory) =>
        _factory = factory;

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task AC1_Ruta_de_la_compania_como_SuperAdmin_responde_404(string method)
    {
        var response = await SendAsync(
            TestTokenFactory.CreateToken("SuperAdmin"), method, $"/api/v1/admin/companies/{Compania}/document-params");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task AC2_Ruta_de_hija_como_SuperAdmin_responde_404(string method)
    {
        var response = await SendAsync(
            TestTokenFactory.CreateToken("SuperAdmin"), method, $"/api/v1/admin/companies/{Cabeza}/children/{Hija}/document-params");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/legal-representatives")]
    [InlineData("/mandate-signers")]
    public async Task AC3_AdminCompany_conserva_representantes_y_mandatarios(string ruta)
    {
        var response = await SendAsync(
            TestTokenFactory.CreateAdminCompanyToken(Compania), "GET", $"/api/v1/admin/companies/{Compania}{ruta}");

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void AC1_AC2_Ningun_endpoint_atiende_document_params()
    {
        Rutas().Should().NotContain(r => r.Contains("document-params", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("{tenantId:guid}/personalized-documents")]
    [InlineData("{tenantId:guid}/ot-prenda-document-policies")]
    [InlineData("/children/{childTenantId:guid}/personalized-documents")]
    [InlineData("/children/{childTenantId:guid}/deeds")]
    [InlineData("/children/{childTenantId:guid}/signature-vault")]
    [InlineData("/children/{childTenantId:guid}/legal-representatives")]
    [InlineData("/children/{childTenantId:guid}/mandate-signers")]
    [InlineData("/children/{childTenantId:guid}/settings")]
    [InlineData("/children/{childTenantId:guid}/whitelist")]
    [InlineData("/children/{childTenantId:guid}/audit-log")]
    public void AC4_Las_rutas_vecinas_siguen_publicadas(string sufijo)
    {
        // Las rutas raíz de un grupo (MapGet("")) quedan con barra final en la plantilla.
        Rutas().Should().Contain(r => r.TrimEnd('/').EndsWith(sufijo, StringComparison.Ordinal));
    }

    private List<string> Rutas()
    {
        _ = _factory.CreateClient();
        return _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .ToList();
    }

    private async Task<HttpResponseMessage> SendAsync(string token, string method, string url)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "PUT")
            request.Content = new StringContent(
                "{\"documentTypeCode\":\"soat\",\"state\":\"OPCIONAL\"}", System.Text.Encoding.UTF8, "application/json");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
