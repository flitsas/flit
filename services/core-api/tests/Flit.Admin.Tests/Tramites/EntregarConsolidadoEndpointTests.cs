using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12785 — contrato HTTP de la ruta de entrega del gestor
/// <c>GET /api/v1/tramites/instances/{id}/consolidado/entrega</c>: el tenant sale del JWT (nunca del
/// header crudo), los flags de query son opcionales, <c>tipo</c> inválido responde 400 y el JSON expone
/// el marcador nuevo <c>definitivoPorEstadoFinal</c> + <c>modo</c> sin romper <c>document</c>/<c>regenerado</c>.
/// Host real sin PostgreSQL: el repositorio es un sustituto (mismo patrón que
/// <see cref="AdminTramitesTenantScopeTests"/>).
/// <para>Uso de ejemplo: <c>GET /api/v1/tramites/instances/{id}/consolidado/entrega?tipo=consolidado_maestro</c>
/// ⇒ <c>200 { document: { attachmentId, … }, regenerado, definitivoPorEstadoFinal, modo }</c>.</para>
/// </summary>
public sealed class EntregarConsolidadoEndpointTests : IClassFixture<AdminTramitesTenantScopeTests.RepoSubstituteFactory>
{
    private static readonly Guid TenantA = Guid.Parse("c0000000-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("c0000000-0000-4000-8000-0000000000b2");

    private readonly AdminTramitesTenantScopeTests.RepoSubstituteFactory _factory;

    public EntregarConsolidadoEndpointTests(AdminTramitesTenantScopeTests.RepoSubstituteFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
    }

    [Fact]
    public async Task SinFlags_ConHeaderAjeno_ConsultaConElTenantDelJwt_Y404SiNoExiste()
    {
        var id = Guid.NewGuid();
        var client = ClientFor(TenantA);

        var response = await client.GetAsync(
            $"/api/v1/tramites/instances/{id}/consolidado/entrega", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "omitir force/soloLectura/tipo no puede dar 400 (Bug #11139)");
        await _factory.Repo.Received(1).GetByIdWithAttachmentsAsync(id, TenantA, Arg.Any<CancellationToken>());
        await _factory.Repo.DidNotReceive().GetByIdWithAttachmentsAsync(id, TenantB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TipoInvalido_400()
    {
        var client = ClientFor(TenantA);

        var response = await client.GetAsync(
            $"/api/v1/tramites/instances/{Guid.NewGuid()}/consolidado/entrega?tipo=fur", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("consolidado", "consolidado")]
    [InlineData("consolidado_maestro", "consolidado_maestro")]
    public async Task AC3_EstadoFinal_200_ConMarcadorDefinitivo_Y_SinRegenerar(string tipoQuery, string tipoAdjunto)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-012785",
            Status = TramiteEstado.Aprobado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var adjunto = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            ProcedureInstanceId = instance.Id,
            Tipo = tipoAdjunto,
            Filename = "definitivo.pdf",
            Mimetype = "application/pdf",
            Sha256 = "sha-definitivo",
            StoragePath = "x/definitivo.pdf",
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(adjunto);
        _factory.Repo.GetByIdWithAttachmentsAsync(instance.Id, TenantA, Arg.Any<CancellationToken>()).Returns(instance);

        var response = await ClientFor(TenantA).GetAsync(
            $"/api/v1/tramites/instances/{instance.Id}/consolidado/entrega?tipo={tipoQuery}&force=true", ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("document").GetProperty("attachmentId").GetGuid().Should().Be(adjunto.Id);
        root.GetProperty("document").GetProperty("tipo").GetString().Should().Be(tipoAdjunto);
        root.GetProperty("regenerado").GetBoolean().Should().BeFalse();
        root.GetProperty("definitivoPorEstadoFinal").GetBoolean().Should().BeTrue();
        root.GetProperty("modo").GetString().Should().Be("definitivo_estado_final");
        await _factory.Repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private HttpClient ClientFor(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId));
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantB.ToString());
        return client;
    }

    private static string Token(Guid tenantId)
    {
        var claims = new List<Claim>
        {
            new("sub", "22222222-2222-2222-2222-222222222222"),
            new("role", "AdminCompany"),
            new("role_code", "AdminCompany"),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange(AdminTramiteAuthorization.AllSlugs.Select(slug => new Claim("permissions", slug)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }
}
