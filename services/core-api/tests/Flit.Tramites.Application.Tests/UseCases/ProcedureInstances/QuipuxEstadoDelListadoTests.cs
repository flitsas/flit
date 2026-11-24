using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// La fila del listado trae <c>quipuxEstado</c> (contrato con el frontend) por las dos rutas (sin y con filtros, esta última
/// también sirve a la vista de red/OT) y la resuelve en UNA consulta en lote por página.
///
/// <para>La regla de negocio (último envío por <c>created_at DESC, id DESC</c>, <c>en_cola</c> solo con canal + integración
/// encendida) vive en el SQL de <c>ProcedureInstanceRepository.GetQuipuxEstadosAsync</c>; la suite no tiene Postgres, así que
/// aquí se cubre el mapeo y el SQL se validó contra <c>flit_v2_local</c> dentro de una transacción con ROLLBACK
/// (sin Quipux, en_cola, integración apagada, fallido, viejo fallido + nuevo registrado, pendiente, rechazado, aprobado).</para>
/// </summary>
public sealed class QuipuxEstadoDelListadoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static ProcedureInstance Instancia(Guid id) => new()
    {
        Id = id,
        ProcedureType = ProcedureTypeFixture.Traspaso,
        TenantId = Guid.NewGuid(),
        ReferenceNumber = "17",
        Status = TramiteEstado.Preparado,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private void ListadoDe(params Guid[] ids) =>
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ids.Select(Instancia).ToList());

    [Fact]
    public async Task Listado_SinQuipux_QuipuxEstadoEsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        ListadoDe(id);
        _repo.GetQuipuxEstadosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string>());

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(Guid.NewGuid(), ct);

        filas.Single().QuipuxEstado.Should().BeNull();
    }

    [Fact]
    public async Task Listado_PreparadoConCanalSinEnvio_EsEnCola()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        ListadoDe(id);
        _repo.GetQuipuxEstadosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [id] = QuipuxListadoEstado.EnCola });

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(Guid.NewGuid(), ct);

        filas.Single().QuipuxEstado.Should().Be("en_cola");
    }

    [Theory]
    [InlineData(QuipuxListadoEstado.Radicado, "radicado")]
    [InlineData(QuipuxListadoEstado.Fallido, "fallido")]
    [InlineData(QuipuxListadoEstado.Rechazado, "rechazado")]
    [InlineData(QuipuxListadoEstado.Aprobado, "aprobado")]
    public async Task Listado_MapeaElEstadoDelUltimoEnvioPorFila(string estado, string esperado)
    {
        var ct = TestContext.Current.CancellationToken;
        var conEnvio = Guid.NewGuid();
        var sinQuipux = Guid.NewGuid();
        ListadoDe(conEnvio, sinQuipux);
        _repo.GetQuipuxEstadosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [conEnvio] = estado });

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(Guid.NewGuid(), ct);

        filas.Single(f => f.Id == conEnvio).QuipuxEstado.Should().Be(esperado);
        filas.Single(f => f.Id == sinQuipux).QuipuxEstado.Should().BeNull();
    }

    [Fact]
    public async Task Listado_ResuelveLaMarcaQuipuxEnUnaSolaConsultaParaTodaLaPagina()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToArray();
        ListadoDe(ids);
        _repo.GetQuipuxEstadosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string>());

        await new ListProcedureInstancesHandler(_repo).HandleAsync(Guid.NewGuid(), ct);

        await _repo.Received(1).GetQuipuxEstadosAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(c => c.Count == 25), ct);
    }

    [Fact]
    public async Task ListadoFiltrado_ConservaLaMarcaQuipux()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), ct)
            .Returns(([Instancia(id)], 1));
        _repo.GetQuipuxEstadosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [id] = QuipuxListadoEstado.Radicado });

        var (items, _) = await new ListProcedureInstancesFilteredHandler(_repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = Guid.NewGuid() }, ct);

        items.Single().QuipuxEstado.Should().Be("radicado");
    }

    [Fact]
    public async Task ListadoDeRed_ConservaLaMarcaQuipux()
    {
        // La vista de red / OT arma sus filas con el mismo `ToSummariesAsync` del listado filtrado.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _repo.GetQuipuxEstadosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [id] = QuipuxListadoEstado.Fallido });

        var items = await ListProcedureInstancesFilteredHandler.ToSummariesAsync(_repo, [Instancia(id)], ct, null);

        items.Single().QuipuxEstado.Should().Be("fallido");
    }
}
