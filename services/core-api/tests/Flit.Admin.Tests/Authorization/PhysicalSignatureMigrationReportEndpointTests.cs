using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13131 (ADR-0061, AC4) — el reporte de migración de la firma física es solo del Super Admin: cualquier
/// otro rol recibe 403 y no ve datos de otros tenants; sin sesión, 401.
/// <para>Uso de ejemplo: <c>GET /api/v1/admin/mandate-signers/physical-signature-migration-report</c> como
/// ot_admin ⇒ 403.</para>
/// </summary>
public sealed class PhysicalSignatureMigrationReportEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/v1/admin/mandate-signers/physical-signature-migration-report";

    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly HttpClient _client;

    public PhysicalSignatureMigrationReportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Authenticate(string role, string? entityType = null) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, Guid.NewGuid(), Guid.NewGuid(), entityType));

    [Theory]
    [InlineData("ot_admin", AdminAuthorization.TransitOfficeEntityType)]
    [InlineData("gestor_tramites_ot", AdminAuthorization.TransitOfficeEntityType)]
    [InlineData("AdminCompany", null)]
    public async Task AC4_UnUsuarioQueNoEsSuperAdmin_Recibe403(string role, string? entityType)
    {
        Authenticate(role, entityType);

        var response = await _client.GetAsync(Url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("mandatario");
    }

    [Fact]
    public async Task AC4_SinSesion_Recibe401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(Url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC2_SuperAdmin_ConsultaElReporte_YLoExportaACsv()
    {
        Authenticate("SuperAdmin");

        var json = await _client.GetAsync(Url, Ct);
        var csv = await _client.GetAsync($"{Url}?format=csv&transitOfficeId={Guid.NewGuid()}", Ct);

        json.StatusCode.Should().Be(HttpStatusCode.OK);
        (await json.Content.ReadAsStringAsync(Ct)).Should().Contain("\"data\"").And.Contain("\"total\"");

        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await csv.Content.ReadAsStringAsync(Ct)).Should().Contain("mandatario_id,mandatario,compania_id");
    }

    private static string MintToken(string role, Guid tenantId, Guid userId, string? entityType)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("role", role),
            new("tenant_id", tenantId.ToString()),
        };
        if (entityType is not null)
        {
            claims.Add(new Claim(AdminAuthorization.EntityTypeClaimType, entityType));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
