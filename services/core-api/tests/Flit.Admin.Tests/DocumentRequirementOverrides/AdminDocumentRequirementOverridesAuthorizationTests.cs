using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.DocumentRequirementOverrides;

/// <summary>
/// Seguridad de los endpoints de obligatoriedad por OT (HU #10198): sin token → 401; rol
/// distinto de SuperAdmin → 403. La autorización corta antes de tocar la base de datos.
/// </summary>
public sealed class AdminDocumentRequirementOverridesAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/v1/admin/document-requirement-overrides";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminDocumentRequirementOverridesAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"{Url}?procedureTypeId={Guid.NewGuid()}&transitOfficeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Set_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(Url, new { procedureTypeId = Guid.NewGuid(), documentTypeId = Guid.NewGuid(), transitOfficeId = Guid.NewGuid(), estado = "REQUIRED" }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync($"{Url}?procedureTypeId={Guid.NewGuid()}&transitOfficeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Set_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PutAsJsonAsync(Url, new { procedureTypeId = Guid.NewGuid(), documentTypeId = Guid.NewGuid(), transitOfficeId = Guid.NewGuid(), estado = "REQUIRED" }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
