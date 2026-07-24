using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Estados;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Sub-estado interno de la ruta de placa (Feature #10587 / HU #10785), ortogonal al status global.
/// Uso de ejemplo:
///   PlateFlowStateMachine.IsValidTransition(null, PlateFlowStatus.Preasignado) → true (Flujo B)
///   PlateFlowStatus.EsValido("asignado") → true
/// </summary>
public sealed class PlateFlowStateMachineTests
{
    // AC1/AC2 — sub-estados válidos y su vocabulario.
    [Fact]
    public void PlateFlowStatus_ReconoceLosSubEstadosNoNulos()
    {
        PlateFlowStatus.Todos.Should().BeEquivalentTo([PlateFlowStatus.Preasignado, PlateFlowStatus.Asignado]);
        PlateFlowStatus.EsValido(PlateFlowStatus.Preasignado).Should().BeTrue();
        PlateFlowStatus.EsValido(PlateFlowStatus.Asignado).Should().BeTrue();
        PlateFlowStatus.EsValido("asignado").Should().BeTrue();
        PlateFlowStatus.EsValido(null).Should().BeFalse();
        PlateFlowStatus.EsValido("").Should().BeFalse();
        PlateFlowStatus.EsValido("Asignado").Should().BeFalse(); // case-sensitive
    }

    // AC4/AC5 — transiciones válidas del sub-flujo (radicación, registro de placa, revocación, cierre).
    [Theory]
    [InlineData(null, "preasignado")]              // radicación Flujo B
    [InlineData(null, "asignado")]                 // radicación Flujo A
    [InlineData("preasignado", "asignado")]        // el OT registra la placa
    [InlineData("asignado", "preasignado")]        // el OT revoca la preasignación
    [InlineData("preasignado", null)]              // cierre del sub-flujo (decisión OT)
    [InlineData("asignado", null)]                 // cierre del sub-flujo (decisión OT)
    public void PlateFlow_TransicionesValidas(string? from, string? to)
    {
        PlateFlowStateMachine.IsValidTransition(from, to).Should().BeTrue();
    }

    // AC3/AC10 — transiciones inválidas del sub-flujo.
    [Theory]
    [InlineData(null, "vigente")]                  // valor desconocido
    [InlineData("preasignado", "inexistente")]     // destino desconocido
    public void PlateFlow_TransicionesInvalidas(string? from, string? to)
    {
        PlateFlowStateMachine.IsValidTransition(from, to).Should().BeFalse();
    }

    // Contrato — la identidad (mismo sub-estado) se considera válida (idempotencia de escritura).
    [Theory]
    [InlineData(null, null)]
    [InlineData("preasignado", "preasignado")]
    [InlineData("asignado", "asignado")]
    public void PlateFlow_Identidad_EsValida(string? from, string? to)
    {
        PlateFlowStateMachine.IsValidTransition(from, to).Should().BeTrue();
    }
}
