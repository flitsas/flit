using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using Flit.Api.Middleware;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12359 (Feature #12257) — capa HTTP de las estadísticas de red, sin PostgreSQL: host real con el
/// repositorio de red sustituido (<see cref="INetworkAnalyticsReadRepository"/>), el resolver de alcance
/// fijo (P ⇒ <c>Group(P,[C1,C2])</c>) y el writer de auditoría capturado.
/// <list type="bullet">
///   <item>AC5 — <c>childTenantId</c> ajeno (X) ⇒ 403 <c>network_child_out_of_scope</c> y el repositorio
///   NO se invoca; el conjunto que recibe el repositorio es siempre un subconjunto del alcance.</item>
///   <item>AC7 — las tres rutas viven bajo <c>/api/v1/tramites/network/stats/*</c>; un cliente sin red
///   (S) recibe 403 <c>network_scope_required</c>; las rutas viejas no cambian.</item>
///   <item>Auditoría (HU #12361) — un desenlace <c>ok</c> con los hijos alcanzados; un rechazo por hijo
///   ajeno deja el intento <c>forbidden</c>.</item>
/// </list>
/// Uso de ejemplo: <c>GET /api/v1/tramites/network/stats/overview?from=2026-09-01&amp;to=2026-09-14&amp;childTenantId={C1}</c>
/// con token de P ⇒ 200 y el repositorio recibe <c>{C1}</c>.
/// </summary>
public sealed class NetworkAnalyticsEndpointsTests : IClassFixture<NetworkAnalyticsEndpointsTests.StatsFactory>
{
    private const string Base = "/api/v1/tramites/network/stats";
    private const string Range = "from=2026-09-01&to=2026-09-14";

    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid X = Guid.Parse("a0000000-0000-4000-8000-000000009900");
    private static readonly Guid S = Guid.Parse("a0000000-0000-4000-8000-000000005500");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly StatsFactory _factory;

    public NetworkAnalyticsEndpointsTests(StatsFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Writer.ClearReceivedCalls();
    }

    public static TheoryData<string> Routes => new()
    {
        $"{Base}/overview",
        $"{Base}/productivity/top",
        $"{Base}/monthly-trend",
    };

    // ── AC7 — rutas nuevas bajo el grupo de red, con la policy de cabeza ─────────────────────

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sin_token_401(string route)
    {
        var response = await _factory.CreateClient().GetAsync($"{route}?{Range}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertRepoNotCalled();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Cliente_sin_red_403_scope_required_sin_consulta(string route)
    {
        var response = await ClientFor(S, "AdminCompany").GetAsync($"{route}?{Range}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ScopeRequired);
        await AssertRepoNotCalled();
        await _factory.Writer.DidNotReceive().WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task SuperAdmin_403_scope_required_las_rutas_globales_son_las_de_siempre(string route)
    {
        var response = await ClientFor(P, "SuperAdmin").GetAsync($"{route}?{Range}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertRepoNotCalled();
    }

    [Fact]
    public void Las_rutas_de_red_estan_bajo_el_prefijo_del_middleware_y_las_viejas_no_cambian()
    {
        TenantEnforcementMiddleware.IsRuntimeScoped($"{Base}/overview").Should().BeTrue();
        TenantEnforcementMiddleware.IsRuntimeScoped($"{Base}/productivity/top").Should().BeTrue();
        TenantEnforcementMiddleware.IsRuntimeScoped($"{Base}/monthly-trend").Should().BeTrue();

        // (ruta, nombre) de los GET: la misma ruta puede tener varios verbos, así que no es un diccionario por ruta.
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => new KeyValuePair<string, string?>(e.RoutePattern.RawText!, e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName))
            .ToList();

        // AC7 — las rutas nuevas existen y son las de red…
        endpoints.Should().Contain($"{Base}/overview", "NetworkStatsOverview")
            .And.Contain($"{Base}/productivity/top", "NetworkStatsTopProducers")
            .And.Contain($"{Base}/monthly-trend", "NetworkStatsMonthlyTrend");
        // …y las actuales siguen siendo las de AnalyticsEndpoints, sin parámetro nuevo (su firma no cambió).
        endpoints.Should().Contain("/api/v1/analytics/overview", "AnalyticsOverview")
            .And.Contain("/api/v1/analytics/productivity/top", "AnalyticsTopProducers")
            .And.Contain("/api/v1/analytics/monthly-trend", "AnalyticsMonthlyTrend");
    }

    // ── AC1 / AC5 — alcance del servidor, subconjunto por hijo, ajeno rechazado sin consulta ──

    [Fact]
    public async Task Overview_Cabeza_200_el_repositorio_recibe_el_alcance_completo_y_la_respuesta_lleva_scope()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{Base}/overview?{Range}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetNetworkOverviewAsync(
            Arg.Is<IReadOnlySet<Guid>>(s => s.SetEquals(new[] { P, C1, C2 })),
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), Arg.Any<CancellationToken>());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = body.RootElement;
        root.GetProperty("tenantId").GetGuid().Should().Be(P, "sin hijo acotado la respuesta identifica a la cabeza");
        root.GetProperty("from").GetString().Should().Be("2026-09-01");
        root.GetProperty("categories").EnumerateArray().Should().HaveCount(1);
        root.GetProperty("categories")[0].GetProperty("category").GetString().Should().Be("matriculas");
        root.GetProperty("categories")[0].GetProperty("total").GetInt32().Should().Be(6);
        root.GetProperty("scope").GetProperty("tenantIds").EnumerateArray().Select(e => e.GetGuid())
            .Should().BeEquivalentTo([P, C1, C2]);
        root.TryGetProperty("reachedTenantIds", out _).Should().BeFalse("los hijos alcanzados son para la auditoría, no para el cliente");

        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Resource == NetworkAccessVocabulary.Resources.StatsOverview
                && e.Result == NetworkAccessVocabulary.Results.Ok && e.ActorTenantId == P
                && e.ReachedTenantIds.Count == 2 && e.ReachedTenantIds.Contains(C1) && e.ReachedTenantIds.Contains(C2)
                && e.FiltersJson != null && e.FiltersJson.Contains("2026-09-01")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Overview_childTenantId_hijo_200_el_repositorio_recibe_solo_ese_hijo()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{Base}/overview?{Range}&childTenantId={C1}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetNetworkOverviewAsync(
            Arg.Is<IReadOnlySet<Guid>>(s => s.Count == 1 && s.Contains(C1)),
            Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("tenantId").GetGuid().Should().Be(C1);
        body.RootElement.GetProperty("scope").GetProperty("tenantIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(C1);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task childTenantId_ajeno_403_child_out_of_scope_sin_consulta_y_con_intento_auditado(string route)
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{route}?{Range}&childTenantId={X}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(NetworkScopePolicy.ChildOutOfScope);
        await AssertRepoNotCalled();
        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Result == NetworkAccessVocabulary.Results.Forbidden
                && e.ReachedTenantIds.Single() == X && e.ActorTenantId == P && e.Resource.StartsWith("network.stats.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_alcance_no_sale_de_la_peticion_ni_X_Tenant_Id_ni_tenantId_amplian()
    {
        var client = ClientFor(P, "AdminCompany");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", X.ToString()); // el middleware lo impone desde el token para usuarios de compañía

        var response = await client.GetAsync($"{Base}/monthly-trend?{Range}&tenantId={X}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetNetworkMonthlyTrendAsync(
            Arg.Is<IReadOnlySet<Guid>>(s => s.SetEquals(new[] { P, C1, C2 }) && !s.Contains(X)),
            Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Top_200_con_limit_acotado_y_scope()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{Base}/productivity/top?{Range}&limit=500&childTenantId={C2}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetNetworkTopProducersAsync(
            Arg.Is<IReadOnlySet<Guid>>(s => s.Count == 1 && s.Contains(C2)),
            Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), GetTopProducersHandler.MaxLimit, Arg.Any<CancellationToken>());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("items").EnumerateArray().Should().HaveCount(1);
        body.RootElement.GetProperty("items")[0].GetProperty("submittedCount").GetInt32().Should().Be(3);
        body.RootElement.GetProperty("scope").GetProperty("tenantIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(C2);
    }

    [Fact]
    public async Task MonthlyTrend_200_items_y_scope()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{Base}/monthly-trend?{Range}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var item = body.RootElement.GetProperty("items")[0];
        item.GetProperty("year").GetInt32().Should().Be(2026);
        item.GetProperty("month").GetInt32().Should().Be(9);
        item.GetProperty("category").GetString().Should().Be("traspasos");
        item.GetProperty("total").GetInt32().Should().Be(4);
        body.RootElement.GetProperty("scope").GetProperty("tenantIds").EnumerateArray().Should().HaveCount(3);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Rango_invalido_400_sin_consulta(string route)
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{route}?from=2026-09-14&to=2026-09-01", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertRepoNotCalled();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task AssertRepoNotCalled()
    {
        await _factory.Repo.DidNotReceive().GetNetworkOverviewAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _factory.Repo.DidNotReceive().GetNetworkTopProducersAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _factory.Repo.DidNotReceive().GetNetworkMonthlyTrendAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

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

    /// <summary>Host con repositorio de red, resolver de alcance y writer de auditoría sustituidos (sin PostgreSQL).</summary>
    public sealed class StatsFactory : WebApplicationFactory<Program>
    {
        public INetworkAnalyticsReadRepository Repo { get; } = Substitute.For<INetworkAnalyticsReadRepository>();

        public INetworkAccessAuditWriter Writer { get; } = Substitute.For<INetworkAccessAuditWriter>();

        public StatsFactory()
        {
            Repo.GetNetworkOverviewAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var set = call.ArgAt<IReadOnlySet<Guid>>(0);
                    IReadOnlyList<CategoryMetricsDto> items = [new("matriculas", 6, [new("borrador", 3), new("entregado", 3)])];
                    return new NetworkAnalyticsResult<IReadOnlyList<CategoryMetricsDto>>(items, set.Where(t => t != P).OrderBy(t => t).ToList());
                });
            Repo.GetNetworkTopProducersAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var set = call.ArgAt<IReadOnlySet<Guid>>(0);
                    IReadOnlyList<TopProducerDto> items = [new(UserId, "Gestor", 3, 1, 0)];
                    return new NetworkAnalyticsResult<IReadOnlyList<TopProducerDto>>(items, set.Where(t => t != P).OrderBy(t => t).ToList());
                });
            Repo.GetNetworkMonthlyTrendAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var set = call.ArgAt<IReadOnlySet<Guid>>(0);
                    IReadOnlyList<MonthlyTrendPointDto> items = [new(2026, 9, "traspasos", 4)];
                    return new NetworkAnalyticsResult<IReadOnlyList<MonthlyTrendPointDto>>(items, set.Where(t => t != P).OrderBy(t => t).ToList());
                });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
                services.AddScoped(_ => Writer);
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
