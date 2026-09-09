using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12162 (AC2 del work item — "Selector con gestores disponibles") — el handler es un envoltorio
/// delgado a propósito: la disponibilidad (activo, no eliminado, sin suspensión vigente, del tenant) ya
/// la filtra <see cref="IProcedureInstanceRepository.ListAvailableGestoresAsync"/> en la consulta.
/// </summary>
public sealed class ListGestoresDisponiblesHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private ListGestoresDisponiblesHandler Handler() => new(_repo);

    [Fact]
    public async Task AC2_DevuelveLosGestoresDisponiblesDelTenant_TalComoLosResuelveElRepositorio()
    {
        var tenantId = Guid.NewGuid();
        var opciones = new List<GestorOption>
        {
            new(Guid.NewGuid(), "Ana Gestora", "ana@empresa.com"),
            new(Guid.NewGuid(), "Beto Gestor", "beto@empresa.com"),
        };
        _repo.ListAvailableGestoresAsync(tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(opciones);

        var result = await Handler().HandleAsync(tenantId, CancellationToken.None);

        result.Should().BeEquivalentTo(opciones);
        await _repo.Received(1).ListAvailableGestoresAsync(
            tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_SinGestoresDisponibles_DevuelveListaVacia()
    {
        var tenantId = Guid.NewGuid();
        _repo.ListAvailableGestoresAsync(tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<GestorOption>());

        var result = await Handler().HandleAsync(tenantId, CancellationToken.None);

        result.Should().BeEmpty();
    }
}
