using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Estados;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Sub-estado interno de la ruta de placa (Feature #10587 + extensión Terminado).
/// </summary>
public sealed class PlateFlowStateMachineTests
{
    [Fact]
    public void PlateFlowStatus_ReconoceLosSubEstadosNoNulos()
    {
        PlateFlowStatus.Todos.Should().BeEquivalentTo(
            [PlateFlowStatus.Preasignado, PlateFlowStatus.Asignado, PlateFlowStatus.Terminado]);
        PlateFlowStatus.EsValido(PlateFlowStatus.Terminado).Should().BeTrue();
        PlateFlowStatus.EsValido(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "preasignado")]
    [InlineData(null, "asignado")]
    [InlineData(null, "terminado")]
    [InlineData("preasignado", "asignado")]
    [InlineData("asignado", "terminado")]
    [InlineData("asignado", "preasignado")]
    [InlineData("preasignado", null)]
    [InlineData("asignado", null)]
    [InlineData("terminado", null)]
    public void PlateFlow_TransicionesValidas(string? from, string? to)
    {
        PlateFlowStateMachine.IsValidTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "vigente")]
    [InlineData("preasignado", "terminado")]
    [InlineData("terminado", "asignado")]
    public void PlateFlow_TransicionesInvalidas(string? from, string? to)
    {
        PlateFlowStateMachine.IsValidTransition(from, to).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("terminado", true)]
    [InlineData("preasignado", false)]
    [InlineData("asignado", false)]
    public void PermiteDecisionOt_SoloNullOTerminado(string? status, bool expected)
    {
        PlateFlowStatus.PermiteDecisionOt(status).Should().Be(expected);
    }
}
