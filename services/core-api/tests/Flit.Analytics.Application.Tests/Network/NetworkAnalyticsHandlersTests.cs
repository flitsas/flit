using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Network;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests.Network;

/// <summary>
/// HU #12359 (Feature #12257) — handlers de estadísticas de red, sin base de datos:
/// <list type="bullet">
///   <item>AC5 — el conjunto que llega al repositorio es siempre un subconjunto del alcance del servidor;
///   un <c>childTenantId</c> ajeno se rechaza SIN invocar el repositorio.</item>
///   <item>AC7 — sin alcance de grupo (SuperAdmin / cliente sin red / sin alcance) ⇒ <c>network_scope_required</c>.</item>
///   <item>Contrato — <c>tenantId</c> de la respuesta, <c>scope.tenantIds</c> ordenado, hijos alcanzados aparte,
///   rango inválido ⇒ <c>invalid_range</c>, límite del Top acotado como en las rutas de siempre.</item>
/// </list>
/// Uso de ejemplo: <c>new GetNetworkAnalyticsOverviewHandler(repo).HandleAsync(new(scope, null, from, to))</c>.
/// </summary>
public sealed class NetworkAnalyticsHandlersTests
{
    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid X = Guid.Parse("a0000000-0000-4000-8000-000000009900");
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 14);

    private readonly INetworkAnalyticsReadRepository _repo = Substitute.For<INetworkAnalyticsReadRepository>();

    private static TenantScope GroupP() => TenantScope.Group(P, [C1, C2], GroupKind.Concesion);

    public NetworkAnalyticsHandlersTests()
    {
        _repo.GetNetworkOverviewAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new NetworkAnalyticsResult<IReadOnlyList<CategoryMetricsDto>>(
                [new("matriculas", 6, [new("borrador", 3), new("entregado", 3)])], [C1, C2]));
        _repo.GetNetworkTopProducersAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new NetworkAnalyticsResult<IReadOnlyList<TopProducerDto>>([new(Guid.NewGuid(), "Gestor", 3, 1, 0)], [C1]));
        _repo.GetNetworkMonthlyTrendAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new NetworkAnalyticsResult<IReadOnlyList<MonthlyTrendPointDto>>([new(2026, 9, "matriculas", 6)], [C1, C2]));
    }

    // ── Happy path ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_Cabeza_consulta_todo_el_alcance_y_responde_con_tenantId_de_la_cabeza_y_scope_ordenado()
    {
        var (result, error) = await new GetNetworkAnalyticsOverviewHandler(_repo)
            .HandleAsync(new GetNetworkAnalyticsOverviewQuery(GroupP(), null, From, To), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Response.TenantId.Should().Be(P);
        result.Response.From.Should().Be(From);
        result.Response.Categories.Should().ContainSingle().Which.Total.Should().Be(6);
        result.Response.Scope.TenantIds.Should().Equal(new[] { P, C1, C2 }.OrderBy(t => t));
        result.ReachedTenantIds.Should().Equal(C1, C2);
        await _repo.Received(1).GetNetworkOverviewAsync(
            Arg.Is<IReadOnlySet<Guid>>(s => s.SetEquals(new[] { P, C1, C2 })), From, To, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Overview_childTenantId_hijo_acota_el_conjunto_y_el_tenantId_de_la_respuesta()
    {
        var (result, error) = await new GetNetworkAnalyticsOverviewHandler(_repo)
            .HandleAsync(new GetNetworkAnalyticsOverviewQuery(GroupP(), C1, From, To), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Response.TenantId.Should().Be(C1);
        result.Response.Scope.TenantIds.Should().Equal(C1);
        await _repo.Received(1).GetNetworkOverviewAsync(
            Arg.Is<IReadOnlySet<Guid>>(s => s.Count == 1 && s.Contains(C1)), From, To, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Top_acota_el_limite_como_las_rutas_de_siempre()
    {
        var handler = new GetNetworkTopProducersHandler(_repo);

        var (porDefecto, _) = await handler.HandleAsync(new GetNetworkTopProducersQuery(GroupP(), null, From, To, 0), TestContext.Current.CancellationToken);
        var (tope, _) = await handler.HandleAsync(new GetNetworkTopProducersQuery(GroupP(), C2, From, To, 9_999), TestContext.Current.CancellationToken);

        porDefecto!.Response.Items.Should().HaveCount(1);
        tope!.Response.Scope.TenantIds.Should().Equal(C2);
        await _repo.Received(1).GetNetworkTopProducersAsync(Arg.Any<IReadOnlySet<Guid>>(), From, To, GetTopProducersHandler.DefaultLimit, Arg.Any<CancellationToken>());
        await _repo.Received(1).GetNetworkTopProducersAsync(Arg.Any<IReadOnlySet<Guid>>(), From, To, GetTopProducersHandler.MaxLimit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MonthlyTrend_devuelve_items_scope_y_alcanzados()
    {
        var (result, error) = await new GetNetworkMonthlyTrendHandler(_repo)
            .HandleAsync(new GetNetworkMonthlyTrendQuery(GroupP(), null, From, To), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Response.Items.Should().ContainSingle().Which.Total.Should().Be(6);
        result.Response.Scope.TenantIds.Should().HaveCount(3);
        result.ReachedTenantIds.Should().Equal(C1, C2);
    }

    // ── AC5 — hijo ajeno: rechazo sin consulta ────────────────────────────────────────────────

    [Fact]
    public async Task childTenantId_ajeno_rechaza_con_child_out_of_scope_sin_invocar_el_repositorio()
    {
        var (overview, e1) = await new GetNetworkAnalyticsOverviewHandler(_repo)
            .HandleAsync(new GetNetworkAnalyticsOverviewQuery(GroupP(), X, From, To), TestContext.Current.CancellationToken);
        var (top, e2) = await new GetNetworkTopProducersHandler(_repo)
            .HandleAsync(new GetNetworkTopProducersQuery(GroupP(), X, From, To, 5), TestContext.Current.CancellationToken);
        var (trend, e3) = await new GetNetworkMonthlyTrendHandler(_repo)
            .HandleAsync(new GetNetworkMonthlyTrendQuery(GroupP(), X, From, To), TestContext.Current.CancellationToken);

        new[] { e1, e2, e3 }.Should().AllBe(NetworkScopePolicy.ChildOutOfScope);
        overview.Should().BeNull();
        top.Should().BeNull();
        trend.Should().BeNull();
        await AssertRepoNotCalled();
    }

    // ── AC7 — sin alcance de grupo ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("single")]
    [InlineData("all")]
    [InlineData("null")]
    public async Task Sin_alcance_de_grupo_rechaza_con_scope_required_sin_invocar_el_repositorio(string kind)
    {
        var scope = kind switch
        {
            "single" => TenantScope.Single(C1),
            "all" => TenantScope.Group(P, [], GroupKind.Concesion), // sin hijos degrada a Single: no hay red
            _ => null,
        };

        var (overview, e1) = await new GetNetworkAnalyticsOverviewHandler(_repo)
            .HandleAsync(new GetNetworkAnalyticsOverviewQuery(scope, null, From, To), TestContext.Current.CancellationToken);
        var (_, e2) = await new GetNetworkTopProducersHandler(_repo)
            .HandleAsync(new GetNetworkTopProducersQuery(scope, null, From, To, 5), TestContext.Current.CancellationToken);
        var (_, e3) = await new GetNetworkMonthlyTrendHandler(_repo)
            .HandleAsync(new GetNetworkMonthlyTrendQuery(scope, null, From, To), TestContext.Current.CancellationToken);

        overview.Should().BeNull();
        new[] { e1, e2, e3 }.Should().AllBe(NetworkScopePolicy.ScopeRequired);
        await AssertRepoNotCalled();
    }

    // ── Contrato — rango inválido ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rango_invalido_devuelve_invalid_range_sin_invocar_el_repositorio()
    {
        var (overview, e1) = await new GetNetworkAnalyticsOverviewHandler(_repo)
            .HandleAsync(new GetNetworkAnalyticsOverviewQuery(GroupP(), null, To, From), TestContext.Current.CancellationToken);
        var (_, e2) = await new GetNetworkTopProducersHandler(_repo)
            .HandleAsync(new GetNetworkTopProducersQuery(GroupP(), C1, To, From, 5), TestContext.Current.CancellationToken);
        var (_, e3) = await new GetNetworkMonthlyTrendHandler(_repo)
            .HandleAsync(new GetNetworkMonthlyTrendQuery(GroupP(), null, To, From), TestContext.Current.CancellationToken);

        overview.Should().BeNull();
        new[] { e1, e2, e3 }.Should().AllBe(NetworkAnalyticsScope.InvalidRange);
        await AssertRepoNotCalled();
    }

    [Fact]
    public void Resolve_el_conjunto_efectivo_es_siempre_subconjunto_del_alcance()
    {
        var (todo, _) = NetworkAnalyticsScope.Resolve(GroupP(), null, From, To);
        var (hijo, _) = NetworkAnalyticsScope.Resolve(GroupP(), C2, From, To);
        var (cabeza, _) = NetworkAnalyticsScope.Resolve(GroupP(), P, From, To);
        var (vacioGuid, _) = NetworkAnalyticsScope.Resolve(GroupP(), Guid.Empty, From, To);
        var (ajeno, errorAjeno) = NetworkAnalyticsScope.Resolve(GroupP(), X, From, To);

        todo!.Should().BeSubsetOf(GroupP().ReadTenantIds).And.HaveCount(3);
        hijo!.Should().Equal(C2);
        cabeza!.Should().Equal(P);
        vacioGuid!.Should().HaveCount(3, "Guid.Empty se trata como «sin filtro», nunca como un cliente");
        ajeno.Should().BeNull();
        errorAjeno.Should().Be(NetworkScopePolicy.ChildOutOfScope);
    }

    private async Task AssertRepoNotCalled()
    {
        await _repo.DidNotReceive().GetNetworkOverviewAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetNetworkTopProducersAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetNetworkMonthlyTrendAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }
}
