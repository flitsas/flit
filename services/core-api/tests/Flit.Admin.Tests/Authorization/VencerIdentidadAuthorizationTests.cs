using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13824 (Feature #13820, Épica #13817) — AC6: «Vencer» una identidad es exclusivo del Super Admin. Un rol de compañía
/// recibe 403 aunque lleve el permiso en el token; el permiso propio es <see cref="IdentidadAdminAuthorization.VencerSlug"/>
/// y no se confunde con el de reenviar. La autorización corta la petición antes de tocar la base de datos: sin PostgreSQL.
/// </summary>
public sealed class VencerIdentidadAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string Url = $"/api/v1/tramites/biometric-validations/{Guid.NewGuid()}/vencer";

    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;

    public VencerIdentidadAuthorizationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Token(string role, bool withPermission, Guid? tenantId = null)
    {
        var claims = new List<Claim>
        {
            new("sub", "11111111-1111-1111-1111-111111111111"),
            new("role", role),
        };
        if (tenantId is not null) claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        if (withPermission) claims.Add(new Claim("permissions", IdentidadAdminAuthorization.VencerSlug));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("admin_tramites")]
    [InlineData("ot_admin")]
    [InlineData("Gerente")]
    public async Task AC6_RolDeCompania_ConElPermisoEnElToken_Returns403(string role)
    {
        var client = ClientWith(Token(role, withPermission: true, Guid.NewGuid()));

        var response = await client.PostAsync(Url, content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC6_RolDeCompania_SinElPermiso_Returns403()
    {
        var client = ClientWith(Token("AdminCompany", withPermission: false, Guid.NewGuid()));

        var response = await client.PostAsync(Url, content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC6_SinToken_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync(Url, content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC6_LaRutaExigeElRolSuperAdmin_YElPermisoPropio_YElSuperAdminLosSatisface()
    {
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var route = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/v1/tramites/biometric-validations/{id:guid}/vencer");

        route.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(a => a.Policy == AdminAuthorization.SuperAdminPolicy,
                "debe exigir el ROL, no solo el permiso: un rol personalizado con el slug asignado a mano no puede usarla");
        route.Metadata.OfType<AuthorizationPolicy>()
            .SelectMany(p => p.Requirements)
            .OfType<PermissionRequirement>()
            .Should().ContainSingle(r => r.Slug == IdentidadAdminAuthorization.VencerSlug);
        IdentidadAdminAuthorization.VencerSlug.Should().NotBe(AdminTramiteAuthorization.ReenviarValidacionSlug,
            "es un permiso nuevo y propio, distinto del de reenviar");

        using var scope = _factory.Services.CreateScope();
        var authz = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var superAdmin = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, AdminAuthorization.SuperAdminRole)], "test", ClaimTypes.Name, ClaimTypes.Role));
        var result = await authz.AuthorizeAsync(superAdmin, null, AdminAuthorization.SuperAdminPolicy);

        result.Succeeded.Should().BeTrue();
    }
}
