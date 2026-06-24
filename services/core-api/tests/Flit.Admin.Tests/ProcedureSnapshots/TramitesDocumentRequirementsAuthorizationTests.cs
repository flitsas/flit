using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.ProcedureSnapshots;

/// <summary>
/// Seguridad de los endpoints de trámites (HU #10197): sin token → 401, tanto en el POST de
/// creación como en el GET de documentos del snapshot. La autorización corta la petición
/// antes de tocar la base de datos (cualquier JWT válido basta; no se exige SuperAdmin).
/// </summary>
public sealed class TramitesDocumentRequirementsAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TramitesUrl = "/api/v1/tramites";

    private readonly WebApplicationFactory<Program> _factory;

    public TramitesDocumentRequirementsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(TramitesUrl, new { procedureTypeId = Guid.NewGuid(), tenantId = Guid.NewGuid(), referenceNumber = "TR-1" }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDocumentRequirements_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"{TramitesUrl}/{Guid.NewGuid()}/document-requirements", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
