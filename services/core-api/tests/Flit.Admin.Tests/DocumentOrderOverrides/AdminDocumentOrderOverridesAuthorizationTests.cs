using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.DocumentOrderOverrides;

/// <summary>
/// Seguridad de los endpoints de órdenes documentales (HU #10196): sin token → 401; rol
/// distinto de SuperAdmin → 403 con mensaje. La autorización corta la petición antes de
/// tocar la base de datos, en overrides y en la matriz resuelta.
/// </summary>
public sealed class AdminDocumentOrderOverridesAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OverridesUrl = "/api/v1/admin/document-order-overrides";
    private const string MatrixUrl = "/api/v1/admin/resolved-document-matrix";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminDocumentOrderOverridesAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Overrides_Create_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{OverridesUrl}?scope=OT", new { procedureTypeId = Guid.NewGuid(), transitOfficeId = Guid.NewGuid(), documentTypeId = Guid.NewGuid(), orden = 1 }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Overrides_List_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"{OverridesUrl}?procedureTypeId={Guid.NewGuid()}&scope=OT&transitOfficeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Matrix_Get_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"{MatrixUrl}?procedureTypeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Overrides_List_WithNonSuperAdminRole_Returns403WithMessage()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync($"{OverridesUrl}?procedureTypeId={Guid.NewGuid()}&scope=OT&transitOfficeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Error.Should().Be("Acceso restringido: se requiere rol SuperAdmin");
    }

    [Fact]
    public async Task Matrix_Get_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync($"{MatrixUrl}?procedureTypeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Overrides_Delete_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.DeleteAsync($"{OverridesUrl}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Overrides_Update_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PutAsJsonAsync($"{OverridesUrl}/{Guid.NewGuid()}", new { orden = 1 }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ErrorBody(string Error);
}
