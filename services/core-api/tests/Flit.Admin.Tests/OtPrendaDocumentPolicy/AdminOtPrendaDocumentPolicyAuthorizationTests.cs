using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.OtPrendaDocumentPolicy;

/// <summary>
/// HU #12859 (Feature #12848, Épica #12751) — el switch de documento de prenda por compañía
/// (<c>/api/v1/admin/transit-offices/{transitOfficeId}/prenda-document-policies</c>) queda
/// EXCLUSIVO de SuperAdmin. Antes admitía <c>ot_admin</c> acotado a su propia OT
/// (<see cref="Flit.Api.Endpoints.AdminOtPrendaDocumentPolicyEndpoints"/>); esa rama de scoping
/// queda sin alcanzar en runtime porque <see cref="AdminAuthorization.SuperAdminPolicy"/> corta
/// antes, en el middleware de autorización.
/// </summary>
public sealed class AdminOtPrendaDocumentPolicyAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdminOtPrendaDocumentPolicyAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static string ListUrl(Guid officeId) =>
        $"/api/v1/admin/transit-offices/{officeId}/prenda-document-policies";

    private static string SetUrl(Guid officeId, Guid tenantId) =>
        $"/api/v1/admin/transit-offices/{officeId}/prenda-document-policies/{tenantId}";

    [Fact]
    public async Task List_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(ListUrl(Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("ot_admin")]
    [InlineData("gestor_tramites_ot")]
    public async Task HU12859_AC1_List_AsNonSuperAdmin_Returns403(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateOtAdminToken(Guid.NewGuid(), role));

        var response = await client.GetAsync(ListUrl(Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ForbiddenBody>(TestContext.Current.CancellationToken);
        body!.Error.Should().Be(AdminAuthorization.OtModuleForbiddenMessage);
    }

    [Theory]
    [InlineData("ot_admin")]
    [InlineData("gestor_tramites_ot")]
    public async Task HU12859_AC1_Set_AsNonSuperAdmin_Returns403(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateOtAdminToken(Guid.NewGuid(), role));

        var response = await client.PutAsJsonAsync(
            SetUrl(Guid.NewGuid(), Guid.NewGuid()),
            new { documentOptional = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HU12859_AC3_List_AsSuperAdmin_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(ListUrl(Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "SuperAdmin sigue operando esta ruta (HU #12859 AC3)");
    }

    [Fact]
    public async Task HU12859_AC3_Set_AsSuperAdmin_DoesNotReturn401Or403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.PutAsJsonAsync(
            SetUrl(Guid.NewGuid(), Guid.NewGuid()),
            new { documentOptional = true },
            TestContext.Current.CancellationToken);

        // El desenlace exacto depende de datos (422 si la OT no existe en el catálogo), pero NUNCA
        // debe ser 401/403: SuperAdmin sigue pasando la autorización.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    private sealed record ForbiddenBody(string Error);
}
