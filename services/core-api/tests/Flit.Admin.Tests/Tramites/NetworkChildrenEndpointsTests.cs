using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.Companies;
using Flit.Api.Middleware;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12555 (Feature #12257, épica #12235) — capa HTTP de <c>GET /api/v1/tramites/network/children</c>,
/// sin PostgreSQL: host real con <see cref="ICompanyHierarchyRepository"/> sustituido y el resolver de
/// alcance fijo (P ⇒ <c>Group(P,[C1,C2])</c>), igual que <c>NetworkAnalyticsEndpointsTests</c>.
/// <list type="bullet">
///   <item>AC1 — cabeza de grupo ⇒ 200 con <c>{id, nombre}</c> de sus hijos, ordenados por nombre,
///   sin exigir ninguna policy admin.</item>
///   <item>AC2 — <c>Single</c> (S) o SuperAdmin ⇒ 403 <c>network_scope_required</c>, SIN llamar al
///   repositorio (misma policy de grupo que las demás rutas de red — <see cref="GroupHeadReadFilter"/>).</item>
///   <item>AC4 — <c>X-Tenant-Id</c> ajeno o <c>?tenantId=</c> en la query no alteran la respuesta: el
///   repositorio siempre recibe el <c>headTenantId</c> del <see cref="TenantScope"/> resuelto en el
///   servidor, nunca el de la petición.</item>
/// </list>
/// Uso de ejemplo: <c>GET /api/v1/tramites/network/children</c> con token de P ⇒ 200 y el repositorio
/// recibe <c>headTenantId = P</c>.
/// </summary>
public sealed class NetworkChildrenEndpointsTests : IClassFixture<NetworkChildrenEndpointsTests.ChildrenFactory>
{
    private const string Route = "/api/v1/tramites/network/children";

    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid X = Guid.Parse("a0000000-0000-4000-8000-000000009900");
    private static readonly Guid S = Guid.Parse("a0000000-0000-4000-8000-000000005500");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ChildrenFactory _factory;

    public NetworkChildrenEndpointsTests(ChildrenFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        // El doble es único por clase (IClassFixture): cada prueba restaura el comportamiento por
        // defecto para no heredar el `Returns` que otra prueba haya dejado configurado.
        _factory.ResetRepoDefaults();
    }

    // ── AC2 — policy de cabeza heredada del grupo ─────────────────────────────────────────────

    [Fact]
    public async Task Sin_token_401()
    {
        var response = await _factory.CreateClient().GetAsync(Route, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertRepoNotCalled();
    }

    [Fact]
    public async Task Cliente_sin_red_Single_403_scope_required_sin_consulta()
    {
        var response = await ClientFor(S, "AdminCompany").GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
        await AssertRepoNotCalled();
    }

    [Fact]
    public async Task SuperAdmin_403_scope_required()
    {
        var response = await ClientFor(P, "SuperAdmin").GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
        await AssertRepoNotCalled();
    }

    // ── AC1 — cabeza de grupo obtiene sus hijos, ordenados por nombre ────────────────────────

    [Fact]
    public async Task Cabeza_200_devuelve_id_y_nombre_de_sus_hijos_ordenados_por_nombre()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).ListChildrenAsync(P, Arg.Any<CancellationToken>());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var items = body.RootElement.EnumerateArray().ToList();
        items.Should().HaveCount(2);
        // El doble devuelve C2 antes que C1 (ver ChildrenFactory); la respuesta debe reordenar por nombre.
        items[0].GetProperty("nombre").GetString().Should().Be("Alfa (C1)");
        items[1].GetProperty("nombre").GetString().Should().Be("Zeta (C2)");
        items[0].GetProperty("id").GetGuid().Should().Be(C1);
        items[1].GetProperty("id").GetGuid().Should().Be(C2);
        // Contrato mínimo: solo id + nombre, sin NIT ni fechas.
        items[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["id", "nombre"]);
    }

    [Fact]
    public async Task Cabeza_sin_hijos_200_lista_vacia()
    {
        _factory.Repo.ListChildrenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompanyChildListItem>());

        var response = await ClientFor(P, "AdminCompany").GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetArrayLength().Should().Be(0);
    }

    // ── AC4 — el alcance nunca sale de la petición ────────────────────────────────────────────

    [Fact]
    public async Task El_alcance_no_sale_de_la_peticion_ni_X_Tenant_Id_ni_tenantId_lo_alteran()
    {
        var client = ClientFor(P, "AdminCompany");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", X.ToString());

        var response = await client.GetAsync($"{Route}?tenantId={X}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).ListChildrenAsync(P, Arg.Any<CancellationToken>());
        await _factory.Repo.DidNotReceive().ListChildrenAsync(X, Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task AssertRepoNotCalled() =>
        await _factory.Repo.DidNotReceive().ListChildrenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

    private HttpClient ClientFor(Guid tenantId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId, role));
        return client;
    }

    private static string Token(Guid tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };

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

    /// <summary>Host con el repositorio de jerarquía y el resolver de alcance sustituidos (sin PostgreSQL).</summary>
    public sealed class ChildrenFactory : WebApplicationFactory<Program>
    {
        public ICompanyHierarchyRepository Repo { get; } = Substitute.For<ICompanyHierarchyRepository>();

        public ChildrenFactory()
        {
            ResetRepoDefaults();
        }

        /// <summary>
        /// Orden de inserción DELIBERADAMENTE distinto al alfabético: prueba que el endpoint reordena
        /// por nombre en vez de devolver lo que entrega el repositorio tal cual. Se reaplica antes de
        /// cada prueba porque el doble es compartido (<see cref="IClassFixture{TFixture}"/>).
        /// </summary>
        public void ResetRepoDefaults() =>
            Repo.ListChildrenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(new List<CompanyChildListItem>
                {
                    new() { Id = C2, Nit = "900000002", RazonSocial = "Zeta (C2)", Code = "C2", TenantType = "CONCESIONARIO", EstadoActivo = true },
                    new() { Id = C1, Nit = "900000001", RazonSocial = "Alfa (C1)", Code = "C1", TenantType = "CONCESIONARIO", EstadoActivo = true },
                });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
                services.AddScoped<ITenantScopeResolver>(_ => new FakeScopeResolver());
            });
        }
    }

    private sealed class FakeScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == P
                ? TenantScope.Group(P, [C1, C2], GroupKind.Concesion)
                : TenantScope.Single(tenantId));
    }
}
