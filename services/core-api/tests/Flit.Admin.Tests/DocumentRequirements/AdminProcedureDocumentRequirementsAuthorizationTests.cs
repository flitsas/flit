using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.DocumentRequirements;

/// <summary>
/// Seguridad de <c>/api/v1/admin/procedure-document-requirements</c> (HU #10195, AC5 /
/// RF17): sin token → 401; rol distinto de SuperAdmin → 403 con mensaje. La
/// autorización corta la petición antes de tocar la base de datos, en todos los
/// métodos del grupo.
/// </summary>
public sealed class AdminProcedureDocumentRequirementsAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string BaseUrl = "/api/v1/admin/procedure-document-requirements";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminProcedureDocumentRequirementsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AC5_List_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"{BaseUrl}?procedureTypeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC5_Create_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(BaseUrl, new { procedureTypeId = Guid.NewGuid(), documentTypeId = Guid.NewGuid(), ordenDefault = 0, obligatorio = true }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC5_List_WithNonSuperAdminRole_Returns403WithMessage()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync($"{BaseUrl}?procedureTypeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Error.Should().Be("Acceso restringido: se requiere rol SuperAdmin");
    }

    [Fact]
    public async Task AC5_Delete_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.DeleteAsync($"{BaseUrl}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ErrorBody(string Error);
}
