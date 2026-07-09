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
    public void SoloColor_ComponeTextoDeColor()
    {
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "PLATA", colorEfectivo: "NEGRO", fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA");

        result.Should().Be("Cambio de color: PLATA → NEGRO.");
    }

    [Fact]
    public void SoloCombustible_ComponeTextoDeCombustible()
    {
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "PLATA", colorEfectivo: "PLATA", fuelRunt: "GASOLINA", fuelEfectivo: "ELECTRICO");

        result.Should().Be("Cambio de combustible: GASOLINA → ELECTRICO.");
    }

    [Fact]
    public void ColorYCombustible_ComponeAmbosEnOrden()
    {
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "plata", colorEfectivo: "negro", fuelRunt: "gasolina", fuelEfectivo: "electrico");

        result.Should().Be("Cambio de color: PLATA → NEGRO. Cambio de combustible: GASOLINA → ELECTRICO.");
    }

    [Fact]
    public void ConObservacionesManuales_AnexaSinBorrar()
    {
        var result = FurTransformationObservations.Compose(
            "  Observación previa.  ",
            colorRunt: "PLATA", colorEfectivo: "NEGRO", fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA");

        result.Should().Be("Observación previa. Cambio de color: PLATA → NEGRO.");
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
