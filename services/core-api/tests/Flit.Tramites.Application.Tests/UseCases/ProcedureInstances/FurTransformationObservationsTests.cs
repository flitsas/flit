using Flit.Tramites.Application.Documents;
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

        result.Should().Be("Color nuevo(NUEVO COLOR: NEGRO)");
    }

    [Fact]
    public void SoloCombustible_ComponeSoloElValorNuevo()
    {
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "PLATA", colorEfectivo: "PLATA", fuelRunt: "GASOLINA", fuelEfectivo: "DIESEL");

        result.Should().Be("COMBUSTIBLE_NUEVO: DIESEL");
    }

    [Fact]
    public void ColorYCombustible_ComponeAmbosSoloValoresNuevos()
    {
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "plata metalico", colorEfectivo: "rojo", fuelRunt: "gasolina", fuelEfectivo: "diesel");

        result.Should().Be("Color nuevo(NUEVO COLOR: ROJO), COMBUSTIBLE_NUEVO: DIESEL");
    }

    [Fact]
    public void ConObservacionesManuales_AnexaSinBorrar()
    {
        var result = FurTransformationObservations.Compose(
            "  Observación previa.  ",
            colorRunt: "PLATA", colorEfectivo: "NEGRO", fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA");

        result.Should().Be("Observación previa. Color nuevo(NUEVO COLOR: NEGRO)");
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

    // ── A4/B4 (HU #10673) — carrocería ────────────────────────────────────────

    [Fact]
    public void SoloCarroceria_ComponeSoloElValorNuevo()
    {
        var result = FurTransformationObservations.Compose(
            null,
            colorRunt: "PLATA", colorEfectivo: "PLATA",
            fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA",
            bodyTypeRunt: "SEDAN", bodyTypeEfectivo: "PICKUP");

        result.Should().Be("Carroceria nueva(NUEVA CARROCERIA: PICKUP)");
    }

    [Fact]
    public void ColorCombustibleYCarroceria_ComponeLosTres()
    {
        var result = FurTransformationObservations.Compose(
            null,
            colorRunt: "plata", colorEfectivo: "negro",
            fuelRunt: "gasolina", fuelEfectivo: "diesel",
            bodyTypeRunt: "sedan", bodyTypeEfectivo: "pickup");

        result.Should().Be("Color nuevo(NUEVO COLOR: NEGRO), Carroceria nueva(NUEVA CARROCERIA: PICKUP), COMBUSTIBLE_NUEVO: DIESEL");
    }

    [Fact]
    public void CarroceriaSnapshotAusente_NoDeclaraCambio()
    {
        var result = FurTransformationObservations.Compose(
            null,
            colorRunt: "PLATA", colorEfectivo: "PLATA",
            fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA",
            bodyTypeRunt: null, bodyTypeEfectivo: "PICKUP");

        result.Should().BeNull();
    }

    [Fact]
    public void CarroceriaIgualSnapshot_NoDeclaraCambio()
    {
        var result = FurTransformationObservations.Compose(
            null,
            colorRunt: "PLATA", colorEfectivo: "PLATA",
            fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA",
            bodyTypeRunt: "SEDAN", bodyTypeEfectivo: " sedan ");

        result.Should().BeNull();
    }

    [Fact]
    public void CarroceriaConObservacionesManuales_AnexaSinBorrar()
    {
        var result = FurTransformationObservations.Compose(
            "Obs previa.",
            colorRunt: "PLATA", colorEfectivo: "PLATA",
            fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA",
            bodyTypeRunt: "SEDAN", bodyTypeEfectivo: "PICKUP");

        result.Should().Be("Obs previa. Carroceria nueva(NUEVA CARROCERIA: PICKUP)");
    }

    [Fact]
    public void SinArgumentosCarroceria_ComportamientoExistenteSinCambio()
    {
        // Sin pasar los parámetros opcionales, la firma de color/combustible funciona igual que antes.
        var result = FurTransformationObservations.Compose(
            null, colorRunt: "PLATA", colorEfectivo: "PLATA", fuelRunt: "GASOLINA", fuelEfectivo: "GASOLINA");

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("SUV", "SUV", true, null)]            // igual al RUNT
    [InlineData("suv", "  SUV ", true, null)]         // igual salvo mayúsculas/espacios
    [InlineData("SUV", "", true, null)]               // efectivo vacío
    [InlineData("SUV", null, true, null)]
    [InlineData("SUV", "PICKUP", false, "PICKUP")]    // con snapshot basta que sea distinto
    [InlineData(null, "PICKUP", true, "PICKUP")]      // sin snapshot: solo si está declarada
    [InlineData(null, "PICKUP", false, null)]
    [InlineData(null, "", true, null)]
    public void ValorNuevo_SoloDevuelveUnValorDistintoDelRunt(
        string? runt, string? efectivo, bool declarada, string? esperado)
    {
        FurTransformationObservations.ValorNuevo(runt, efectivo, declarada).Should().Be(esperado);
    }

    [Fact]
    public void ComposeDeclaradas_SinValorNuevo_NoEscribeLaLinea()
    {
        var texto = FurTransformationObservations.ComposeDeclaradas(
            new FurTransformacionesDeclaradas(Color: true, Carroceria: true, Combustible: true),
            FurTransformationObservations.ValorNuevo("ROJO", "rojo", true),
            FurTransformationObservations.ValorNuevo("GASOLINA", "GASOLINA", true),
            FurTransformationObservations.ValorNuevo("SUV", "SUV", true));

        texto.Should().BeNull();
    }

    [Fact]
    public void ComposeDeclaradas_ConcatenaSinReemplazar()
    {
        var texto = FurTransformationObservations.ComposeDeclaradas(
            new FurTransformacionesDeclaradas(Color: true, Carroceria: true, Combustible: true),
            "MULTICOLOR CON AEROGRAFIAS",
            "DIESEL",
            "PICKUP");

        texto.Should().Be(
            "Color nuevo(NUEVO COLOR: MULTICOLOR CON AEROGRAFIAS), Carroceria nueva(NUEVA CARROCERIA: PICKUP), COMBUSTIBLE_NUEVO: DIESEL");
    }
}
