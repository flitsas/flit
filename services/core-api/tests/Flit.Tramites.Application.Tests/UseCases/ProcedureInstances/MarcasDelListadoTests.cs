using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12182 — las marcas de prenda y transformación llegan a la fila del listado por las
/// <b>dos</b> rutas (con y sin filtros) y la de prenda cuesta <b>una</b> consulta, no una por fila.
///
/// <para>Esa última comprobación es el motivo de que estos tests existan: la marca es correcta y el
/// listado se ve bien igual si se resolviera fila a fila. Lo que se rompería es el tiempo de
/// respuesta con un tenant grande, y eso no lo delata ninguna aserción sobre el contenido.</para>
/// </summary>
public sealed class MarcasDelListadoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static ProcedureInstance Instancia(
        Guid id, ProcedureType tipo, params (string Clave, string Valor)[] campos)
    {
        var instancia = new ProcedureInstance
        {
            Id = id,
            ProcedureType = tipo,
            TenantId = Guid.NewGuid(),
            ReferenceNumber = "17",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var (clave, valor) in campos)
        {
            instancia.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                FieldKey = clave,
                ValueText = valor,
            });
        }

        return instancia;
    }

    // ── Listado sin filtros ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Listado_MarcaLaPrendaSoloEnLaInstanciaQueLaTiene()
    {
        var ct = TestContext.Current.CancellationToken;
        var conPrenda = Guid.NewGuid();
        var sinPrenda = Guid.NewGuid();

        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([
            Instancia(conPrenda, ProcedureTypeFixture.Traspaso),
            Instancia(sinPrenda, ProcedureTypeFixture.Traspaso),
        ]);
        _repo.ListInstanceIdsConPrendaVigenteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new HashSet<Guid> { conPrenda });

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(Guid.NewGuid(), ct);

        filas.Single(f => f.Id == conPrenda).TienePrenda.Should().BeTrue();
        filas.Single(f => f.Id == sinPrenda).TienePrenda.Should().BeFalse();
    }

    [Fact]
    public async Task Listado_LaPrendaSeResuelveEnUnaSolaConsultaParaTodoElListado()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToList();

        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct)
            .Returns(ids.Select(id => Instancia(id, ProcedureTypeFixture.Traspaso)).ToList());
        _repo.ListInstanceIdsConPrendaVigenteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new HashSet<Guid>());

        await new ListProcedureInstancesHandler(_repo).HandleAsync(Guid.NewGuid(), ct);

        // Una llamada, con los 25 ids dentro. No 25 llamadas de un id.
        await _repo.Received(1).ListInstanceIdsConPrendaVigenteAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(c => c.Count == 25), ct);
    }

    [Fact]
    public async Task Listado_MarcaLaTransformacionDeclaradaSinConsultaAdicional()
    {
        var ct = TestContext.Current.CancellationToken;
        var conCambioColor = Guid.NewGuid();
        var corriente = Guid.NewGuid();

        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([
            Instancia(conCambioColor, ProcedureTypeFixture.Traspaso, ("cambio_color", "true")),
            Instancia(corriente, ProcedureTypeFixture.Traspaso),
        ]);
        _repo.ListInstanceIdsConPrendaVigenteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new HashSet<Guid>());

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(Guid.NewGuid(), ct);

        filas.Single(f => f.Id == conCambioColor).TieneTransformacion.Should().BeTrue();
        filas.Single(f => f.Id == corriente).TieneTransformacion.Should().BeFalse();
    }

    [Fact]
    public async Task Listado_UnaInstanciaPuedeLlevarLasDosMarcas()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([
            Instancia(id, ProcedureTypeFixture.Traspaso, ("blindaje", "true")),
        ]);
        _repo.ListInstanceIdsConPrendaVigenteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new HashSet<Guid> { id });

        var fila = (await new ListProcedureInstancesHandler(_repo).HandleAsync(Guid.NewGuid(), ct)).Single();

        fila.TienePrenda.Should().BeTrue();
        fila.TieneTransformacion.Should().BeTrue();
    }

    // ── Listado FILTRADO ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListadoFiltrado_ConservaLasMarcas()
    {
        // Las dos rutas comparten `ToSummary`, pero cada una arma por su cuenta lo que le pasa: si
        // esta se quedara sin la marca, los íconos desaparecerían en cuanto se aplicara un filtro.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), ct)
            .Returns(([Instancia(id, ProcedureTypeFixture.Traspaso, ("cambio_carroceria", "true"))], 1));
        _repo.ListInstanceIdsConPrendaVigenteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new HashSet<Guid> { id });

        var (items, _) = await new ListProcedureInstancesFilteredHandler(_repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = Guid.NewGuid() }, ct);

        items.Single().TienePrenda.Should().BeTrue();
        items.Single().TieneTransformacion.Should().BeTrue();
    }
}
