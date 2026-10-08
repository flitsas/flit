using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13383 (épica #13216, ADR-0070 A4.1) — <see cref="SuperAdminSeleccionResolver"/>: el origen <c>superadmin</c> usa
/// el MISMO núcleo del listado que <c>tramites</c>; lo único propio es el tenant, que es el scope de
/// <c>X-Tenant-Id</c> (o <c>null</c> = todas las compañías). Repositorio mockeado.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var sut = new SuperAdminSeleccionResolver(new ListProcedureInstancesFilteredHandler(repo), repo);
/// var refs = await sut.ResolverAsync(new SeleccionPorIds(ids), new LoteSeleccionContexto(scopeOrNull, sa), ct);
/// </code>
/// </remarks>
public sealed class SuperAdminSeleccionResolverTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid SuperAdmin = Guid.NewGuid();

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly SuperAdminSeleccionResolver _sut;

    public SuperAdminSeleccionResolverTests()
    {
        _sut = new SuperAdminSeleccionResolver(new ListProcedureInstancesFilteredHandler(_repo), _repo);
        _repo.ListIdsFilteredAsync(Arg.Any<Guid?>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([new ProcedureInstanceRef(Guid.NewGuid(), TenantA, "R-A", null), new ProcedureInstanceRef(Guid.NewGuid(), TenantB, "R-B", null)]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Origen_EsSuperadmin_YConviveConElDeTramitesEnElSelector()
    {
        _sut.Origen.Should().Be(ConsolidadoExportOrigin.Superadmin);
        var porOrigen = new LoteSeleccionResolverPorOrigen(
            [new TramitesSeleccionResolver(new ListProcedureInstancesFilteredHandler(_repo), _repo), _sut]);
        porOrigen.Atiende(ConsolidadoExportOrigin.Superadmin).Should().BeTrue();
        porOrigen.Para(ConsolidadoExportOrigin.Superadmin).Should().BeSameAs(_sut);
    }

    [Fact]
    public async Task Ids_SinScope_ConsultaSinTenant_YDevuelveTramitesDeVariasCompanias()
    {
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var refs = await _sut.ResolverAsync(new SeleccionPorIds(ids), new LoteSeleccionContexto(null, SuperAdmin), Ct);

        refs.Select(r => r.TenantId).Should().BeEquivalentTo([TenantA, TenantB]);
        await _repo.Received(1).ListIdsFilteredAsync(null,
            Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos != null && f.IdsIncluidos.Count == 2),
            ProcedureInstanceSortBy.Default, SortDirection.Descending, Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ids_ConScopeB_ConsultaConElTenantB()
    {
        await _sut.ResolverAsync(new SeleccionPorIds([Guid.NewGuid()]), new LoteSeleccionContexto(TenantB, SuperAdmin), Ct);

        await _repo.Received(1).ListIdsFilteredAsync(TenantB, Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().ListIdsFilteredAsync(null, Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Filtro_ConCondicionCompania_PasaLaCondicionAlListadoSinTenant()
    {
        var compania = new QueryCondition(TramitesQueryFieldCatalog.Compania, QueryOperator.EsAlguno, [TenantB.ToString()]);
        var filtro = new TramitesLoteFiltro(new ProcedureInstanceListRequest { Condiciones = [compania], TenantId = TenantA });

        await _sut.ResolverAsync(new SeleccionPorFiltro(filtro, []), new LoteSeleccionContexto(null, SuperAdmin), Ct);

        await _repo.Received(1).ListIdsFilteredAsync(null,
            Arg.Is<ProcedureInstanceListFilter>(f => f.Condiciones != null && f.Condiciones.Any(c => c.FieldId == "compania")),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Topes_YFiltroInvalido_SeAplicanIgualQueEnTramites()
    {
        var ids = Enumerable.Range(0, LoteSeleccionTopes.MaxIds + 1).Select(_ => Guid.NewGuid()).ToList();
        var tope = () => _sut.ResolverAsync(new SeleccionPorIds(ids), new LoteSeleccionContexto(null, SuperAdmin), Ct);
        (await tope.Should().ThrowAsync<LoteSeleccionInvalidaException>()).Which.Codigo
            .Should().Be(LoteSeleccionInvalidaException.CodigoExcedeTope);

        var malo = new TramitesLoteFiltro(new ProcedureInstanceListRequest
        {
            Condiciones = [new QueryCondition("no_existe", QueryOperator.EsAlguno, ["x"])],
        });
        var invalido = () => _sut.ResolverAsync(new SeleccionPorFiltro(malo, []), new LoteSeleccionContexto(null, SuperAdmin), Ct);
        (await invalido.Should().ThrowAsync<LoteSeleccionInvalidaException>()).Which.Codigo
            .Should().Be(LoteSeleccionInvalidaException.CodigoFiltroInvalido);
    }

    /// <summary>Code review épica #13216 (Obs2) — el límite y el conteo del 422 se delegan en el resolver de tramites.</summary>
    [Fact]
    public async Task Obs2_DelegaElLimiteYElConteo_SinScopeEsTodaLaPlataforma()
    {
        _repo.CountIdsFilteredAsync(null, Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<CancellationToken>()).Returns(120_000);
        var seleccion = new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest()), []);

        await _sut.ResolverAsync(seleccion, new LoteSeleccionContexto(null, SuperAdmin), 10_001, Ct);
        var total = await _sut.ContarAsync(seleccion, new LoteSeleccionContexto(null, SuperAdmin), Ct);

        total.Should().Be(120_000);
        await _repo.Received(1).ListIdsFilteredAsync(null, Arg.Any<ProcedureInstanceListFilter>(),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), 10_001, Arg.Any<CancellationToken>());
    }
}
