using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Auditing;

/// <summary>
/// Tests de seguridad de <c>GET /api/v1/superadmin/audit</c> (HU #10679, AC1) — la ruta
/// vive bajo el grupo <c>/api/v1/superadmin</c>, que exige <c>AdminAuthorization
/// .SuperAdminPolicy</c> a nivel de grupo (ver <see cref="SecurityAuditEndpoints"/> y
/// <c>SuperAdminEndpointExtensions</c>). Mismo patrón que
/// <c>AdminCompaniesAuthorizationTests</c> (HU #10189): sin token → 401; con rol
/// distinto de SuperAdmin → 403 con el mensaje estándar
/// <see cref="AdminAuthorization.ForbiddenMessage"/>. La autorización corta la petición
/// antes de tocar la base de datos, así que estos tests no requieren PostgreSQL.
/// </summary>
public sealed class SecurityAuditEndpointsAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AuditUrl = "/api/v1/superadmin/audit";

    private readonly WebApplicationFactory<Program> _factory;

    public SecurityAuditEndpointsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AC1_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(AuditUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC1_WithNonSuperAdminRole_Returns403WithMessage()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(AuditUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Error.Should().Be("Acceso restringido: se requiere rol SuperAdmin");
    }

    [Fact]
    public async Task AC1_WithOtAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(Guid.NewGuid()));

        var response = await client.GetAsync(AuditUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ErrorBody(string Error);
}
