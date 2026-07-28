using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Tramites.Services;

/// <summary>
/// HU #10872 (AC1) — mapeo campo→gate para la re-radicación selectiva desde subsanación.
/// </summary>
public sealed class SubsanacionGateMapTests
{
    [Fact]
    public void ResolveGates_SinCamposCambiados_NoResuelveNingunGate()
    {
        SubsanacionGateMap.ResolveGates(new HashSet<string>()).Should().BeEmpty();
    }

    [Fact]
    public void ResolveGates_VinCambiado_ResuelveSoloVehicleState()
    {
        var gates = SubsanacionGateMap.ResolveGates(new HashSet<string> { "vin" });

        gates.Should().ContainSingle().Which.Should().Be(SubsanacionGateMap.VehicleState);
    }

    [Fact]
    public void ResolveGates_VinCaseInsensitive_ResuelveVehicleState()
    {
        var gates = SubsanacionGateMap.ResolveGates(new HashSet<string> { "VIN" });

        gates.Should().Contain(SubsanacionGateMap.VehicleState);
    }

    [Theory]
    [InlineData("transit_office_id")]
    [InlineData("color")]
    [InlineData("chasis")]
    public void ResolveGates_CualquierOtroCampo_ResuelvePreparationGate(string fieldKey)
    {
        var gates = SubsanacionGateMap.ResolveGates(new HashSet<string> { fieldKey });

        gates.Should().ContainSingle().Which.Should().Be(SubsanacionGateMap.PreparationGate);
    }

    [Fact]
    public void ResolveGates_VinYOtroCampo_ResuelveAmbasCategorias()
    {
        var gates = SubsanacionGateMap.ResolveGates(new HashSet<string> { "vin", "transit_office_id" });

        gates.Should().BeEquivalentTo([SubsanacionGateMap.VehicleState, SubsanacionGateMap.PreparationGate]);
    }

    [Fact]
    public void NoBaselineFallback_SoloIncluyeVehicleState()
    {
        // Preserva el comportamiento previo a HU #10872: VehicleState corría siempre para
        // cualquier transición a entregado; PreparationGate nunca corría para subsanacion→entregado.
        SubsanacionGateMap.NoBaselineFallback.Should().BeEquivalentTo([SubsanacionGateMap.VehicleState]);
    }
}
