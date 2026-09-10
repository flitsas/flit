using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Lista blanca de <c>sortBy</c> (RNF: un valor no reconocido —typo o intento de inyección— cae al
/// orden por defecto, NUNCA se concatena en SQL).
/// </summary>
public sealed class ProcedureInstanceSortFieldsTests
{
    [Theory]
    [InlineData("comprador", ProcedureInstanceSortBy.Comprador)]
    [InlineData("Comprador", ProcedureInstanceSortBy.Comprador)]
    [InlineData("createdAt", ProcedureInstanceSortBy.CreatedAt)]
    [InlineData("created_at", ProcedureInstanceSortBy.CreatedAt)]
    [InlineData("updatedAt", ProcedureInstanceSortBy.UpdatedAt)]
    [InlineData("updated_at", ProcedureInstanceSortBy.UpdatedAt)]
    [InlineData("gestor", ProcedureInstanceSortBy.Gestor)]
    [InlineData("placa", ProcedureInstanceSortBy.Placa)]
    [InlineData("plate", ProcedureInstanceSortBy.Placa)]
    [InlineData("vin", ProcedureInstanceSortBy.Vin)]
    [InlineData("VIN", ProcedureInstanceSortBy.Vin)]
    public void Resolve_ValorDeLaWhitelist_MapeaAlCampoEsperado(string sortBy, ProcedureInstanceSortBy esperado) =>
        ProcedureInstanceSortFields.Resolve(sortBy).Should().Be(esperado);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("campoInexistente")]
    // Intentos de "inyección" vía el parámetro sortBy: como la resolución es un lookup en diccionario
    // (nunca concatenación de string en SQL), lo peor que puede pasar es caer al orden por defecto.
    [InlineData("created_at; DROP TABLE tramites.procedure_instances;--")]
    [InlineData("' OR '1'='1")]
    public void Resolve_ValorNoReconocido_CaeAlDefault(string? sortBy) =>
        ProcedureInstanceSortFields.Resolve(sortBy).Should().Be(ProcedureInstanceSortBy.Default);
}

/// <summary>
/// Orquestación de <see cref="ListProcedureInstancesFilteredHandler"/>: arma el filtro/orden a partir
/// del request, delega el WHERE/ORDER BY al repositorio (mockeado — la traducción a SQL real la cubren
/// las pruebas de <c>Flit.Infrastructure.Tests</c>) y reutiliza el mismo mapeo a
/// <see cref="InstanceSummaryDto"/> que <see cref="ListProcedureInstancesHandler"/>.
/// </summary>
public sealed class ListProcedureInstancesFilteredHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ListProcedureInstancesFilteredHandler _sut;

    public ListProcedureInstancesFilteredHandlerTests()
    {
        _sut = new ListProcedureInstancesFilteredHandler(_repo);
    }

    private static ProcedureInstance Instancia(Guid tenantId, string reference) => new()
    {
        ProcedureType = ProcedureTypeFixture.For(TramiteModalidadEntradaCodes.MatriculaInicial),
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ReferenceNumber = reference,
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task HandleAsync_SinResultados_DevuelveVacioYTotalCero()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        _repo.ListWithSummaryGraphFilteredAsync(
                tenantId, 0, ListProcedureInstancesHandler.MaxItems,
                Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), ct)
            .Returns(((IReadOnlyList<ProcedureInstance>)[], 0));

        var (items, total) = await _sut.HandleAsync(new ProcedureInstanceListRequest { TenantId = tenantId }, ct);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_MapeaFiltrosYOrdenSortByInvalidoCaeADefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instancia = Instancia(tenantId, "TRM-2026-000001");

        _repo.ListWithSummaryGraphFilteredAsync(
                tenantId, 0, ListProcedureInstancesHandler.MaxItems,
                Arg.Is<ProcedureInstanceListFilter>(f => f.Vin == "ABC123" && f.Comprador == "juan"),
                ProcedureInstanceSortBy.Default,
                SortDirection.Descending,
                ct)
            .Returns(((IReadOnlyList<ProcedureInstance>)[instancia], 1));

        var request = new ProcedureInstanceListRequest
        {
            TenantId = tenantId,
            Vin = "ABC123",
            Comprador = "juan",
            SortBy = "no-existe",
        };

        var (items, total) = await _sut.HandleAsync(request, ct);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.ReferenceNumber == "TRM-2026-000001");
    }

    [Fact]
    public async Task HandleAsync_SortDescendingFalse_PideAscendente()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();

        _repo.ListWithSummaryGraphFilteredAsync(
                tenantId, 0, ListProcedureInstancesHandler.MaxItems,
                Arg.Any<ProcedureInstanceListFilter>(),
                ProcedureInstanceSortBy.Vin,
                SortDirection.Ascending,
                ct)
            .Returns(((IReadOnlyList<ProcedureInstance>)[], 0));

        await _sut.HandleAsync(
            new ProcedureInstanceListRequest { TenantId = tenantId, SortBy = "vin", SortDescending = false }, ct);

        await _repo.Received(1).ListWithSummaryGraphFilteredAsync(
            tenantId, 0, ListProcedureInstancesHandler.MaxItems,
            Arg.Any<ProcedureInstanceListFilter>(), ProcedureInstanceSortBy.Vin, SortDirection.Ascending, ct);
    }

    [Fact]
    public async Task HandleAsync_TakeFueraDeRango_SeAcotaAlMaximo()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();

        _repo.ListWithSummaryGraphFilteredAsync(
                tenantId, 0, ListProcedureInstancesHandler.MaxItems,
                Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), ct)
            .Returns(((IReadOnlyList<ProcedureInstance>)[], 0));

        await _sut.HandleAsync(
            new ProcedureInstanceListRequest { TenantId = tenantId, Take = ListProcedureInstancesHandler.MaxItems + 500 }, ct);

        await _repo.Received(1).ListWithSummaryGraphFilteredAsync(
            tenantId, 0, ListProcedureInstancesHandler.MaxItems,
            Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), ct);
    }

    // ── HU #12162 — el gestor efectivo también en la ruta FILTRADA ───────────────────────────
    //
    // La HU #12162 puso el gestor efectivo (AssignedToUserId ?? CreatedByUserId) en el listado sin
    // filtros, pero esta ruta se quedó con el creador. Como las dos comparten `ToSummary` y pintan la
    // MISMA columna, la consecuencia era que un trámite reasignado cambiaba de gestor en pantalla en
    // cuanto se aplicaba cualquier filtro. Estas pruebas son el espejo de las de
    // `ListProcedureInstancesTests` para que las dos rutas no puedan volver a separarse.

    [Fact]
    public async Task HandleAsync_TramiteReasignado_GestorNombreEsElAsignadoNoElCreador()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var creador = Guid.NewGuid();
        var asignado = Guid.NewGuid();
        var instancia = Instancia(tenantId, "R1");
        instancia.CreatedByUserId = creador;
        instancia.AssignedToUserId = asignado;

        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<ProcedureInstanceSortBy>(),
                Arg.Any<SortDirection>(), ct)
            .Returns(((IReadOnlyList<ProcedureInstance>)[instancia], 1));
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string>
            {
                [creador] = "Quien Radicó",
                [asignado] = "Gestor Reasignado",
            });

        var (items, _) = await _sut.HandleAsync(new ProcedureInstanceListRequest { TenantId = tenantId }, ct);

        items[0].GestorNombre.Should().Be("Gestor Reasignado");
        // El lote se pide por el id EFECTIVO, igual que en la ruta sin filtros.
        await _repo.Received(1).GetUserDisplayNamesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(asignado)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TramiteNuncaReasignado_GestorNombreCaeAlCreador()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var creador = Guid.NewGuid();
        var instancia = Instancia(tenantId, "R1");
        instancia.CreatedByUserId = creador;
        instancia.AssignedToUserId = null;

        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<ProcedureInstanceSortBy>(),
                Arg.Any<SortDirection>(), ct)
            .Returns(((IReadOnlyList<ProcedureInstance>)[instancia], 1));
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [creador] = "Quien Radicó" });

        var (items, _) = await _sut.HandleAsync(new ProcedureInstanceListRequest { TenantId = tenantId }, ct);

        items[0].GestorNombre.Should().Be("Quien Radicó");
    }

    [Fact]
    public async Task HandleAsync_LasDosRutasResuelvenElMismoGestor()
    {
        // El punto de la HU: la columna no puede depender de si hay filtros aplicados. Se corre la
        // MISMA instancia por las dos rutas y se exige el mismo nombre.
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var creador = Guid.NewGuid();
        var asignado = Guid.NewGuid();
        var nombres = new Dictionary<Guid, string>
        {
            [creador] = "Quien Radicó",
            [asignado] = "Gestor Reasignado",
        };

        ProcedureInstance NuevaInstancia()
        {
            var i = Instancia(tenantId, "R1");
            i.CreatedByUserId = creador;
            i.AssignedToUserId = asignado;
            return i;
        }

        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<ProcedureInstanceSortBy>(),
                Arg.Any<SortDirection>(), ct)
            .Returns(((IReadOnlyList<ProcedureInstance>)[NuevaInstancia()], 1));
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct)
            .Returns([NuevaInstancia()]);
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct).Returns(nombres);

        var (conFiltros, _) = await _sut.HandleAsync(
            new ProcedureInstanceListRequest { TenantId = tenantId }, ct);
        var sinFiltros = await new ListProcedureInstancesHandler(_repo).HandleAsync(tenantId, ct);

        conFiltros[0].GestorNombre.Should().Be(sinFiltros[0].GestorNombre);
        conFiltros[0].GestorNombre.Should().Be("Gestor Reasignado");
    }
}
