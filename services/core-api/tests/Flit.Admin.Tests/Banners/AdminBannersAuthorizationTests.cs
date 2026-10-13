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

namespace Flit.Admin.Tests.Banners;

/// <summary>
/// HU #13438 (Feature #13436, Épica #12750) — la gestión de banners es exclusiva del Super Admin FLIT.
/// La autorización corta la petición antes de tocar la base de datos, así que estos tests no requieren PostgreSQL.
/// </summary>
public sealed class AdminBannersAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string BannersUrl = "/api/v1/admin/banners";
    private const string BannersManage = "banners.manage";

    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;

    public AdminBannersAuthorizationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    /// <summary>Token con rol y, opcionalmente, el permiso <c>banners.manage</c> en el claim <c>permissions</c>.</summary>
    private static string Token(string role, bool withBannersPermission, Guid? tenantId = null)
    {
        var claims = new List<Claim>
        {
            new("sub", "11111111-1111-1111-1111-111111111111"),
            new("role", role),
        };
        if (tenantId is not null) claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        if (withBannersPermission) claims.Add(new Claim("permissions", BannersManage));

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

    // AC2 — negativo: ningún rol distinto de SuperAdmin, ni siquiera con el permiso en el token.

    [Fact]
    public async Task AC2_AdminCompanyConPermisoBannersManage_Returns403()
    {
        var client = ClientWith(Token("AdminCompany", withBannersPermission: true, Guid.NewGuid()));

        var response = await client.GetAsync(BannersUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC2_OtAdminConPermisoBannersManage_Returns403()
    {
        var client = ClientWith(Token("ot_admin", withBannersPermission: true, Guid.NewGuid()));

        var response = await client.GetAsync(BannersUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC2_RolPersonalizadoConPermisoBannersManage_Returns403()
    {
        var client = ClientWith(Token("Gerente", withBannersPermission: true, Guid.NewGuid()));

        var response = await client.GetAsync(BannersUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task AC2_Escrituras_ConAdminCompany_Returns403(string method)
    {
        var client = ClientWith(Token("AdminCompany", withBannersPermission: true, Guid.NewGuid()));
        var suffix = method == "POST" ? string.Empty : $"/{Guid.NewGuid()}{(method == "PATCH" ? "/active" : string.Empty)}";

        using var request = new HttpRequestMessage(new HttpMethod(method), BannersUrl + suffix);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC2_SinToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync(BannersUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // AC1 — positivo: SuperAdmin pasa la autorización (sin depender de la base de datos: se verifica la política
    // de cada ruta de gestión y que el rol SuperAdmin la satisface).

    [Fact]
    public async Task AC1_TodasLasRutasDeGestion_ExigenSuperAdminPolicy_YSuperAdminLaSatisface()
    {
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var routes = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith(BannersUrl, StringComparison.Ordinal))
            .ToList();

        routes.Should().HaveCount(5, "crear, listar, editar, activar/desactivar y eliminar");
        foreach (var route in routes)
        {
            route.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Should().Contain(a => a.Policy == AdminAuthorization.SuperAdminPolicy,
                    $"{route.RoutePattern.RawText} debe exigir el rol, no solo el permiso");
        }

        using var scope = _factory.Services.CreateScope();
        var authz = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var superAdmin = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, AdminAuthorization.SuperAdminRole)], "test", ClaimTypes.Name, ClaimTypes.Role));
        var result = await authz.AuthorizeAsync(superAdmin, null, AdminAuthorization.SuperAdminPolicy);

        result.Succeeded.Should().BeTrue();
    }
}
