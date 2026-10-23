using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13145 (ADR-0066) — contrato HTTP del firmante previsto. Host real sin
/// PostgreSQL con el repositorio sustituido (mismo patrón que <see cref="EntregarConsolidadoEndpointTests"/>):
/// <c>GET /api/v1/tramites/instances/{id}/mandate-signer</c> busca el trámite con el tenant del JWT (otro
/// tenant = 404) y las rutas retiradas por la HU #13156 (lista de candidatos y PUT del mandatario) ya no se atienden.
/// </summary>
public sealed class MandatarioPrevistoEndpointsTests : IClassFixture<AdminTramitesTenantScopeTests.RepoSubstituteFactory>
{
    private static readonly Guid TenantA = Guid.Parse("c0000000-0000-4000-8000-0000000013a1");
    private static readonly Guid TenantB = Guid.Parse("c0000000-0000-4000-8000-0000000013b2");

    private readonly AdminTramitesTenantScopeTests.RepoSubstituteFactory _factory;

    public MandatarioPrevistoEndpointsTests(AdminTramitesTenantScopeTests.RepoSubstituteFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
    }

    [Fact]
    public async Task Ac5_GetDeOtroTenant_Responde404_YSeBuscaConElTenantDelJwt()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        var response = await ClientFor(TenantA).GetAsync($"/api/v1/tramites/instances/{id}/mandate-signer", ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.Repo.Received(1).GetByIdWithDetailsAsync(id, TenantA, Arg.Any<CancellationToken>());
        await _factory.Repo.DidNotReceive().GetByIdWithDetailsAsync(id, TenantB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HU13156_Ac1_GetDeLaListaDeMandatarios_Responde404()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        var response = await ClientFor(TenantA).GetAsync($"/api/v1/tramites/instances/{id}/mandate-signers", ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.Repo.DidNotReceiveWithAnyArgs().GetByIdWithDetailsAsync(default, default(Guid), default);
    }

    [Fact]
    public async Task HU13156_Ac1_PutQueFijabaElMandatario_YaNoSeAtiende_YNoTocaElTramite()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        var response = await ClientFor(TenantA).PutAsJsonAsync(
            $"/api/v1/tramites/instances/{id}/mandate-signer", new { mandateSignerId = Guid.NewGuid() }, ct);

        // La ruta sigue existiendo solo para GET (firmante previsto): un PUT ya no llega a ningún handler.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        await _factory.Repo.DidNotReceiveWithAnyArgs().GetByIdWithDetailsAsync(default, default(Guid), default);
        await _factory.Repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
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
