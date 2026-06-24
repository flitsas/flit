using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.DocumentTypes;

/// <summary>
/// Seguridad de <c>/api/v1/admin/document-types</c> (HU #10193, AC5 / RF17):
/// sin token → 401; rol distinto de SuperAdmin → 403 con mensaje. La autorización
/// corta la petición antes de tocar la base de datos, en todos los métodos del grupo.
/// </summary>
public sealed class AdminDocumentTypesAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string BaseUrl = "/api/v1/admin/document-types";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminDocumentTypesAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AC5_List_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC5_Create_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(BaseUrl, new { codigo = "RUT", nombre = "Doc", descripcion = (string?)null }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC5_List_WithNonSuperAdminRole_Returns403WithMessage()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task Reactivate_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PostAsync($"{BaseUrl}/{Guid.NewGuid()}/reactivate", content: null, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ErrorBody(string Error);
}
