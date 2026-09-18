using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// HU #11228 — AdminCompany opera su tenant y no el de otro. HU #12710 reserva la configuración
/// (settings y demás secciones) al SuperAdmin y a la cabeza de red: el AdminCompany sin red ya no la
/// lee ni en su propio tenant (la suite completa vive en <c>AdminCompanyReservedSectionsTests</c>).
/// </summary>
public sealed class AdminCompanyOwnTenantAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid OwnTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherTenant = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly WebApplicationFactory<Program> _factory;

    public AdminCompanyOwnTenantAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Settings_AdminCompany_sin_red_OwnTenant_Returns403()
    {
        // HU #12710 (D4) — antes 200 (HU #11228); la configuración la gestiona el SuperAdmin.
        var client = Client(OwnTenant);
        var response = await client.GetAsync(
            $"/api/v1/admin/companies/{OwnTenant}/settings",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Settings_AdminCompany_OtherTenant_Returns403()
    {
        var client = Client(OwnTenant);
        var response = await client.GetAsync(
            $"/api/v1/admin/companies/{OtherTenant}/settings",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Index_AdminCompany_Returns403()
    {
        var client = Client(OwnTenant);
        var response = await client.GetAsync(
            "/api/v1/admin/companies/index",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateAdminCompanyToken(tenantId));
        return client;
    }
}
