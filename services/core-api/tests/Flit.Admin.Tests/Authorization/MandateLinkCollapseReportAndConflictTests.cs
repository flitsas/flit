using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Admin.Application.Companies.MandateSigners.PhysicalSignatureMigration;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13195 (ADR-0066 D1) — reporte previo del colapso solo para Super Admin (AC1) y traducción a 409 del
/// rechazo del índice «un activo por origen» (AC3).
/// <para>Uso de ejemplo: <c>GET /api/v1/admin/mandate-signers/link-collapse-report</c> como ot_admin ⇒ 403.</para>
/// </summary>
public sealed class MandateLinkCollapseReportAndConflictTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/v1/admin/mandate-signers/link-collapse-report";

    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly HttpClient _client;

    public MandateLinkCollapseReportAndConflictTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Authenticate(string role, string? entityType = null) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, Guid.NewGuid(), Guid.NewGuid(), entityType));

    // ── AC1: el reporte es del Super Admin ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ot_admin", AdminAuthorization.TransitOfficeEntityType)]
    [InlineData("gestor_tramites_ot", AdminAuthorization.TransitOfficeEntityType)]
    [InlineData("AdminCompany", null)]
    public async Task AC1_UnUsuarioQueNoEsSuperAdmin_Recibe403(string role, string? entityType)
    {
        Authenticate(role, entityType);

        var response = await _client.GetAsync(Url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC1_SinSesion_Recibe401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        (await _client.GetAsync(Url, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC1_Handler_OrdenaConservarPrimeroYNoExponeDatosPersonales()
    {
        var office = Guid.NewGuid();
        var company = Guid.NewGuid();
        var kept = new MandateLinkCollapseRow(office, "OT-1", company, "organismo", 2, Guid.NewGuid(), Guid.NewGuid(),
            MandateLinkCollapseActions.Conservar, MandateLinkCollapseCriteria.DesignadoEnRegla);
        var dropped = kept with
        {
            LinkId = Guid.NewGuid(),
            MandateSignerId = Guid.NewGuid(),
            Action = MandateLinkCollapseActions.Inactivar,
        };
        var reader = Substitute.For<IMandateSignerLinkCollapseReader>();
        reader.ListAsync(null, Arg.Any<CancellationToken>()).Returns([dropped, kept]);

        var rows = await new GetMandateLinkCollapseReportHandler(reader).HandleAsync(null, Ct);

        rows.Select(r => r.Action).Should().Equal(
            MandateLinkCollapseActions.Conservar, MandateLinkCollapseActions.Inactivar);
        typeof(MandateLinkCollapseRow).GetProperties().Select(p => p.Name).Should().NotContain(
            n => n.Contains("Document") || n.Contains("Name") && n != "TransitOfficeName" || n.Contains("Email"),
            "el reporte solo lleva ids, organismo, compañía y decisión (Ley 1581)");
        await reader.Received(1).ListAsync(null, Arg.Any<CancellationToken>());
    }

    // ── AC3: el rechazo del índice es 409 con mensaje claro, sin datos personales ─────────────────

    [Fact]
    public async Task AC3_FiltroConvierteElConflictoDelIndiceEn409ConMensajeClaro()
    {
        var filter = new MandateSignerLinkConflictFilter();
        var http = new DefaultHttpContext();
        var context = new DefaultEndpointFilterInvocationContext(http);

        var result = await filter.InvokeAsync(
            context,
            _ => throw new MandateSignerActiveLinkConflictException());

        var json = result.Should().BeAssignableTo<IResult>().Subject;
        http.Response.Body = new MemoryStream();
        http.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        await json.ExecuteAsync(http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync(Ct);
        body.Should().Contain("mandatario_activo_existente").And.Contain("Ya existe un mandatario activo");
    }

    [Fact]
    public async Task AC3_FiltroDejaPasarLoQueNoEsElConflicto()
    {
        var filter = new MandateSignerLinkConflictFilter();
        var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("ok"));

        result.Should().Be("ok");
        var act = async () => await filter.InvokeAsync(context, _ => throw new InvalidOperationException("otra"));
        await act.Should().ThrowAsync<InvalidOperationException>();
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
