using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>A4/B4 (HU #10673, ADR-0029) — composición del texto automático de observaciones FUR.</summary>
public sealed class FurTransformationObservationsTests
{
    [Fact]
    public void SinCambios_DevuelveObservacionesManualesSinTocar()
    {
        var result = FurTransformationObservations.Compose(
            "Nota manual del operador",
            colorRunt: "PLATA", colorEfectivo: "PLATA",
            fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA");

        result.Should().Be("Nota manual del operador");
    }

    [Fact]
    public void SinCambiosYSinManuales_DevuelveNull()
    {
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "PLATA", colorEfectivo: "PLATA", fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA");

        result.Should().BeNull();
    }

    [Fact]
    public void SoloColor_ComponeSoloElValorNuevo()
    {
        // Solo el valor NUEVO (efectivo): sin flecha ni valor RUNT (el campo del FUR ya lleva el original).
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "PLATA", colorEfectivo: "NEGRO", fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA");

        result.Should().Be("Cambio de color: NEGRO.");
    }

    [Fact]
    public void SoloCombustible_ComponeSoloElValorNuevo()
    {
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "PLATA", colorEfectivo: "PLATA", fuelRunt: "GASOLINA", fuelEfectivo: "DIESEL");

        result.Should().Be("Cambio de combustible: DIESEL.");
    }

    [Fact]
    public void ColorYCombustible_ComponeAmbosSoloValoresNuevos()
    {
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "plata metalico", colorEfectivo: "rojo", fuelRunt: "gasolina", fuelEfectivo: "diesel");

        result.Should().Be("Cambio de color: ROJO. Cambio de combustible: DIESEL.");
    }

    [Fact]
    public void ConObservacionesManuales_AnexaSinBorrar()
    {
        var result = FurTransformationObservations.Compose(
            "  Observación previa.  ",
            colorRunt: "PLATA", colorEfectivo: "NEGRO", fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA");

        result.Should().Be("Observación previa. Cambio de color: NEGRO.");
    }

    [Fact]
    public void SnapshotAusente_NoDeclaraCambio()
    {
        // Sin snapshot RUNT no se puede afirmar que hubo cambio: no se compone texto.
        var result = FurTransformationObservations.Compose(
            null, colorRunt: null, colorEfectivo: "NEGRO", fuelRunt: "", fuelEfectivo: "ELECTRICO");

        result.Should().BeNull();
    }

    [Fact]
    public void DiferenciaSoloPorMayusculasOEspacios_NoEsCambio()
    {
        var result = FurTransformationObservations.Compose(
            "Manual", colorRunt: "PLATA", colorEfectivo: " plata ", fuelRunt: "GASOLINA", fuelEfectivo: "gasolina");

        result.Should().Be("Manual");
    }
}
