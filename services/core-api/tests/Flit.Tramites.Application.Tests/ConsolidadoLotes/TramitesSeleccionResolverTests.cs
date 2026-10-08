using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13370 (épica #13216) — <see cref="TramitesSeleccionResolver"/>: la selección del lote se resuelve con el
/// MISMO filtro, búsqueda rápida y orden del listado, con el tenant del token y con los topes de 10.000.
/// El repositorio va mockeado; la traducción a consulta real (orden, tenant, borrado lógico) la cubre
/// <c>ProcedureInstanceListIdsFilteredRepositoryTests</c> en <c>Flit.Infrastructure.Tests</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var sut = new TramitesSeleccionResolver(new ListProcedureInstancesFilteredHandler(repo), repo);
/// var refs = await sut.ResolverAsync(new SeleccionPorIds(ids), new LoteSeleccionContexto(tenant, user), ct);
/// </code>
/// </remarks>
public sealed class TramitesSeleccionResolverTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly TramitesSeleccionResolver _sut;
    private static readonly Guid TenantC = Guid.NewGuid();
    private static readonly Guid UsuarioC = Guid.NewGuid();

    public TramitesSeleccionResolverTests()
    {
        _sut = new TramitesSeleccionResolver(new ListProcedureInstancesFilteredHandler(_repo), _repo);
    }

    private static List<ProcedureInstanceRef> Refs(int n, Guid tenant) =>
        Enumerable.Range(1, n)
            .Select(i => new ProcedureInstanceRef(Guid.NewGuid(), tenant, $"R-{i:D5}", $"AB{i:D4}"))
            .ToList();

    private void RepoDevuelve(IReadOnlyList<ProcedureInstanceRef> refs) =>
        _repo.ListIdsFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(refs);

    // ── AC1 — selección por filtro sin tope de página ────────────────────────────────────

    [Fact]
    public async Task Filtro_350MenosDosExcluidos_Devuelve348EnElOrdenDelListado()
    {
        var ct = TestContext.Current.CancellationToken;
        var refs = Refs(350, TenantC);
        RepoDevuelve(refs);
        var excluidos = new[] { refs[10].Id, refs[200].Id };

        var resultado = await _sut.ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest()), excluidos),
            new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        resultado.Should().HaveCount(348);
        resultado.Should().Equal(refs.Where(r => !excluidos.Contains(r.Id)),
            "se conserva el orden del listado y solo se quitan los excluidos");
    }

    [Fact]
    public async Task Filtro_UsaElMismoFiltroYOrdenQueElListado_YElTenantDelToken()
    {
        var ct = TestContext.Current.CancellationToken;
        RepoDevuelve([]);
        var tenantDelCuerpo = Guid.NewGuid();
        var criterios = new ProcedureInstanceListRequest
        {
            TenantId = tenantDelCuerpo, // nunca se usa: el tenant sale del token
            Skip = 400,
            Take = 50,
            Placa = "ABC123",
            Estados = ["borrador"],
            SortBy = "placa",
            SortDescending = false,
        };

        await _sut.ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(criterios)),
            new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        await _repo.Received(1).ListIdsFilteredAsync(
            TenantC,
            Arg.Is<ProcedureInstanceListFilter>(f =>
                f.Placa == "ABC123" && f.Estados != null && f.Estados.Count == 1 && f.Estados[0] == "borrador"),
            ProcedureInstanceSortBy.Placa,
            SortDirection.Ascending,
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().ListIdsFilteredAsync(
            tenantDelCuerpo, Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Filtro_MisTramites_UsaElUsuarioDelToken()
    {
        var ct = TestContext.Current.CancellationToken;
        RepoDevuelve([]);

        await _sut.ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest
            {
                BusquedaRapida = BusquedaRapida.MisTramites,
                UsuarioActualId = Guid.NewGuid(), // del cuerpo: se ignora
            })),
            new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        await _repo.Received(1).ListIdsFilteredAsync(
            TenantC, Arg.Is<ProcedureInstanceListFilter>(f => f.ResponsableId == UsuarioC),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    // ── AC2 — selección manual acotada a la compañía ─────────────────────────────────────

    [Fact]
    public async Task Ids_SeResuelvenContraElRepositorioConElTenantDelTokenYSinFiltrosDelListado()
    {
        var ct = TestContext.Current.CancellationToken;
        var deC = Refs(3, TenantC);
        var idDeD = Guid.NewGuid();
        RepoDevuelve(deC); // el repositorio acota por tenant: el id de D no vuelve
        var ids = deC.Select(r => r.Id).Append(idDeD).ToList();

        var resultado = await _sut.ResolverAsync(
            new SeleccionPorIds(ids), new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        resultado.Select(r => r.Id).Should().BeEquivalentTo(deC.Select(r => r.Id));
        resultado.Select(r => r.Id).Should().NotContain(idDeD);
        await _repo.Received(1).ListIdsFilteredAsync(
            TenantC,
            Arg.Is<ProcedureInstanceListFilter>(f =>
                f.IdsIncluidos != null && f.IdsIncluidos.Count == 4 && f.IdsIncluidos.Contains(idDeD)
                && f.Estados == null && f.Placa == null && f.Condiciones == null),
            ProcedureInstanceSortBy.Default, SortDirection.Descending, Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ids_Vacios_NoConsultaYDevuelveVacio()
    {
        var ct = TestContext.Current.CancellationToken;

        var resultado = await _sut.ResolverAsync(
            new SeleccionPorIds([]), new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        resultado.Should().BeEmpty();
        await _repo.DidNotReceiveWithAnyArgs().ListIdsFilteredAsync(default, default!, default, default, default, ct);
    }

    // ── AC3 — topes de 10.000 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ids_10001_SeRechazaConErrorDeValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = Enumerable.Range(0, LoteSeleccionTopes.MaxIds + 1).Select(_ => Guid.NewGuid()).ToList();

        var act = () => _sut.ResolverAsync(new SeleccionPorIds(ids), new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        var ex = (await act.Should().ThrowAsync<LoteSeleccionInvalidaException>()).Which;
        ex.Codigo.Should().Be(LoteSeleccionInvalidaException.CodigoExcedeTope);
        ex.Message.Should().Contain("10001").And.Contain("10000");
        await _repo.DidNotReceiveWithAnyArgs().ListIdsFilteredAsync(default, default!, default, default, default, ct);
    }

    [Fact]
    public async Task Ids_Exactamente10000_SeAceptan()
    {
        var ct = TestContext.Current.CancellationToken;
        RepoDevuelve([]);
        var ids = Enumerable.Range(0, LoteSeleccionTopes.MaxIds).Select(_ => Guid.NewGuid()).ToList();

        var act = () => _sut.ResolverAsync(new SeleccionPorIds(ids), new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Excluidos_10001_SeRechazaConErrorDeValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var excluidos = Enumerable.Range(0, LoteSeleccionTopes.MaxExcluidos + 1).Select(_ => Guid.NewGuid()).ToList();

        var act = () => _sut.ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest()), excluidos),
            new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        var ex = (await act.Should().ThrowAsync<LoteSeleccionInvalidaException>()).Which;
        ex.Codigo.Should().Be(LoteSeleccionInvalidaException.CodigoExcedeTope);
        ex.Message.Should().Contain("excluye");
    }

    [Fact]
    public async Task Filtro_QueResuelve25000_NoSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        RepoDevuelve(Refs(25_000, TenantC));

        var resultado = await _sut.ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest())),
            new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        resultado.Should().HaveCount(25_000, "el modo filtro no tiene tope de resultados");
    }

    // ── AC4 — búsqueda rápida demasiado amplia ───────────────────────────────────────────

    [Fact]
    public async Task Filtro_BusquedaRapidaDemasiadoAmplia_SePropaga()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), 0, BusquedaRapidaResolver.TopeBorradores,
                Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<ProcedureInstanceSortBy>(),
                Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstance>)[], BusquedaRapidaResolver.TopeBorradores + 1));

        var act = () => _sut.ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(
                new ProcedureInstanceListRequest { BusquedaRapida = BusquedaRapida.SinFirmas })),
            new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        await act.Should().ThrowAsync<BusquedaRapidaDemasiadoAmpliaException>();
        await _repo.DidNotReceiveWithAnyArgs().ListIdsFilteredAsync(default, default!, default, default, default, ct);
    }

    // ── Contrato — filtros fuera de catálogo, carga de otro origen y registro por origen ──

    [Fact]
    public async Task Filtro_ConCondicionFueraDeCatalogo_SeRechazaEnVezDeIgnorarse()
    {
        var ct = TestContext.Current.CancellationToken;

        var act = () => _sut.ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest
            {
                Condiciones = [new QueryCondition("campo_inexistente", "eq", ["x"])],
            })),
            new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        (await act.Should().ThrowAsync<LoteSeleccionInvalidaException>())
            .Which.Codigo.Should().Be(LoteSeleccionInvalidaException.CodigoFiltroInvalido);
    }

    [Fact]
    public async Task Filtro_DeOtroOrigen_NoSeAcepta()
    {
        var ct = TestContext.Current.CancellationToken;

        var act = () => _sut.ResolverAsync(
            new SeleccionPorFiltro(new FiltroDeOtroOrigen()), new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void PorOrigen_EligeElResolverPorClave_YFallaConOrigenDesconocidoODuplicado()
    {
        var porOrigen = new LoteSeleccionResolverPorOrigen([_sut]);

        porOrigen.Para("tramites").Should().BeSameAs(_sut);
        _sut.Origen.Should().Be(TramitesSeleccionResolver.OrigenTramites);
        FluentActions.Invoking(() => porOrigen.Para("ot_bandeja")).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new LoteSeleccionResolverPorOrigen([_sut, _sut]))
            .Should().Throw<InvalidOperationException>();
    }

    // ── Code review épica #13216 (Obs2) — límite de lectura y conteo del 422 ──────────────────

    [Fact]
    public async Task Obs2_Filtro_PideAlRepositorioElLimiteMasLosExcluidos_YLosResta()
    {
        var ct = TestContext.Current.CancellationToken;
        var refs = Refs(8, TenantC);
        RepoDevuelve(refs);
        var excluidos = new[] { refs[2].Id, Guid.NewGuid() };

        var resultado = await _sut.ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest()), excluidos),
            new LoteSeleccionContexto(TenantC, UsuarioC), 6, ct);

        resultado.Should().Equal(refs.Where(r => r.Id != refs[2].Id),
            "con limite + excluidos leídos, quitar los excluidos deja al menos el límite si la selección lo alcanza");
        await _repo.Received(1).ListIdsFilteredAsync(
            TenantC, Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(),
            8, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Obs2_Ids_PasanElLimiteTalCual_YSinLimiteSeLeeTodo()
    {
        var ct = TestContext.Current.CancellationToken;
        RepoDevuelve(Refs(2, TenantC));

        await _sut.ResolverAsync(new SeleccionPorIds([Guid.NewGuid(), Guid.NewGuid()]), new LoteSeleccionContexto(TenantC, UsuarioC), 5, ct);
        await _sut.ResolverAsync(new SeleccionPorIds([Guid.NewGuid()]), new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        await _repo.Received(1).ListIdsFilteredAsync(
            TenantC, Arg.Any<ProcedureInstanceListFilter>(), ProcedureInstanceSortBy.Default, SortDirection.Descending,
            5, Arg.Any<CancellationToken>());
        await _repo.Received(1).ListIdsFilteredAsync(
            TenantC, Arg.Any<ProcedureInstanceListFilter>(), ProcedureInstanceSortBy.Default, SortDirection.Descending,
            null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Obs2_Contar_Filtro_EsElConteoDelMismoFiltroMenosLosExcluidosQueLoCumplen()
    {
        var ct = TestContext.Current.CancellationToken;
        var excluidos = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _repo.CountIdsFilteredAsync(TenantC, Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos == null && f.Placa == "ABC123"),
            Arg.Any<CancellationToken>()).Returns(350);
        _repo.CountIdsFilteredAsync(TenantC,
            Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos != null && f.IdsIncluidos.Count == 3 && f.Placa == "ABC123"),
            Arg.Any<CancellationToken>()).Returns(2);

        var total = await _sut.ContarAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest { Placa = "ABC123", TenantId = Guid.NewGuid() }), excluidos),
            new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        total.Should().Be(348, "350 del filtro menos los 2 excluidos que lo cumplen; el tercero no estaba");
        await _repo.DidNotReceiveWithAnyArgs().ListIdsFilteredAsync(default, default!, default, default, default, ct);
    }

    [Fact]
    public async Task Obs2_Contar_Filtro_ConAtajoQueYaAcotaIds_IntersecaLosExcluidosConEsosIds()
    {
        var ct = TestContext.Current.CancellationToken;
        var dentro = Guid.NewGuid();
        var fuera = Guid.NewGuid();
        // «Mis trámites» sin usuario deja IdsIncluidos = [] (nada): los excluidos se intersecan con ese vacío y la
        // intersección vacía no necesita una segunda consulta.
        _repo.CountIdsFilteredAsync(TenantC, Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<CancellationToken>()).Returns(0);

        var total = await _sut.ContarAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest { BusquedaRapida = BusquedaRapida.MisTramites }),
                [dentro, fuera]),
            new LoteSeleccionContexto(TenantC, null), ct);

        total.Should().Be(0);
        await _repo.Received(1).CountIdsFilteredAsync(TenantC,
            Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos != null && f.IdsIncluidos.Count == 0), Arg.Any<CancellationToken>());
        await _repo.Received(1).CountIdsFilteredAsync(TenantC, Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Obs2_Contar_Ids_CuentaLaInterseccionConLaCompania_YValidaComoElResolver()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        _repo.CountIdsFilteredAsync(TenantC, Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos != null && f.IdsIncluidos.Count == 2),
            Arg.Any<CancellationToken>()).Returns(1);

        var total = await _sut.ContarAsync(new SeleccionPorIds([a, Guid.NewGuid(), a]), new LoteSeleccionContexto(TenantC, UsuarioC), ct);
        var vacio = await _sut.ContarAsync(new SeleccionPorIds([]), new LoteSeleccionContexto(TenantC, UsuarioC), ct);
        var invalido = () => _sut.ContarAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest
            {
                Condiciones = [new QueryCondition("campo_inexistente", "eq", ["x"])],
            })),
            new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        total.Should().Be(1);
        vacio.Should().Be(0);
        (await invalido.Should().ThrowAsync<LoteSeleccionInvalidaException>())
            .Which.Codigo.Should().Be(LoteSeleccionInvalidaException.CodigoFiltroInvalido);
    }

    // ── HU #13417 — alcance de la vista de red (TenantScope del middleware, nunca del cuerpo) ───────────

    private static readonly Guid CabezaP = Guid.NewGuid();
    private static readonly Guid HijaC1 = Guid.NewGuid();
    private static readonly Guid HijaC2 = Guid.NewGuid();

    private static Flit.Queries.Domain.Tenancy.TenantScope AlcanceRed() =>
        Flit.Queries.Domain.Tenancy.TenantScope.Group(CabezaP, [HijaC1, HijaC2], Flit.Queries.Domain.Tenancy.GroupKind.MarcaBlanca);

    private void RepoEnRedDevuelve(IReadOnlyList<ProcedureInstanceRef> refs) =>
        _repo.ListIdsFilteredInScopeAsync(
                Arg.Any<Flit.Queries.Domain.Tenancy.TenantScope>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(refs);

    [Fact]
    public async Task HU13417_AC3_IdsConAlcanceDeRed_ResuelveContraElAlcance_NuncaContraElTenantSolo()
    {
        var ct = TestContext.Current.CancellationToken;
        var alcance = AlcanceRed();
        var refs = new List<ProcedureInstanceRef> { new(Guid.NewGuid(), CabezaP, "R-1", null), new(Guid.NewGuid(), HijaC1, "R-2", null) };
        RepoEnRedDevuelve(refs);

        var resultado = await _sut.ResolverAsync(
            new SeleccionPorIds(refs.Select(r => r.Id).ToList()),
            new LoteSeleccionContexto(CabezaP, UsuarioC, null, alcance), 11, ct);

        resultado.Should().Equal(refs);
        await _repo.Received(1).ListIdsFilteredInScopeAsync(
            alcance, Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos != null && f.IdsIncluidos.Count == 2),
            ProcedureInstanceSortBy.Default, SortDirection.Descending, 11, Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().ListIdsFilteredAsync(default, default!, default, default, default, ct);
    }

    [Fact]
    public async Task HU13417_AC1_FiltroConAlcanceDeRed_UsaElUniversoDelListadoDeRed_ConLosExcluidosRestados()
    {
        var ct = TestContext.Current.CancellationToken;
        var alcance = AlcanceRed();
        var refs = new List<ProcedureInstanceRef>
        {
            new(Guid.NewGuid(), CabezaP, "R-1", null), new(Guid.NewGuid(), HijaC1, "R-2", null), new(Guid.NewGuid(), HijaC2, "R-3", null),
        };
        RepoEnRedDevuelve(refs);

        var resultado = await _sut.ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest { Placa = "ABC123" }), [refs[1].Id]),
            new LoteSeleccionContexto(CabezaP, UsuarioC, null, alcance), 5, ct);

        resultado.Should().Equal(refs[0], refs[2]);
        await _repo.Received(1).ListIdsFilteredInScopeAsync(
            alcance, Arg.Is<ProcedureInstanceListFilter>(f => f.Placa == "ABC123" && f.IdsIncluidos == null),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), 6, Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().ListIdsFilteredAsync(default, default!, default, default, default, ct);
    }

    [Fact]
    public async Task HU13417_AC11_ContarConAlcanceDeRed_CuentaConElMismoPredicadoDeRed()
    {
        var ct = TestContext.Current.CancellationToken;
        var alcance = AlcanceRed();
        var excluidos = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _repo.CountIdsFilteredInScopeAsync(alcance, Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos == null), Arg.Any<CancellationToken>())
            .Returns(40);
        _repo.CountIdsFilteredInScopeAsync(alcance, Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos != null && f.IdsIncluidos.Count == 2),
            Arg.Any<CancellationToken>()).Returns(1);

        var porFiltro = await _sut.ContarAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest()), excluidos),
            new LoteSeleccionContexto(CabezaP, UsuarioC, null, alcance), ct);
        var porIds = await _sut.ContarAsync(
            new SeleccionPorIds(excluidos), new LoteSeleccionContexto(CabezaP, UsuarioC, null, alcance), ct);

        porFiltro.Should().Be(39);
        porIds.Should().Be(1);
        await _repo.DidNotReceiveWithAnyArgs().CountIdsFilteredAsync(default, default!, ct);
    }

    [Fact]
    public async Task HU13417_AC10_SinAlcanceDeRed_NoTocaLaConsultaDeRed()
    {
        var ct = TestContext.Current.CancellationToken;
        RepoDevuelve(Refs(1, TenantC));

        await _sut.ResolverAsync(new SeleccionPorIds([Guid.NewGuid()]), new LoteSeleccionContexto(TenantC, UsuarioC), ct);

        await _repo.DidNotReceiveWithAnyArgs().ListIdsFilteredInScopeAsync(default!, default!, default, default, default, ct);
    }

    private sealed record FiltroDeOtroOrigen : LoteFiltro;
}
