using Flit.Tramites.Application.BulkTramites.Parsing;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.BulkTramites;

/// <summary>
/// Réplica —sobre la fila del Excel, antes de que exista el trámite— de las reglas de reparto de
/// propiedad de ADR-0053 / <c>PutActorsHandler</c> (HU #12522, AC3).
/// </summary>
public sealed class BulkTramitesPercentageValidatorTests
{
    private static Dictionary<string, string?> Fila(params (string Key, string Value)[] pares) =>
        pares.ToDictionary(p => p.Key, p => (string?)p.Value);

    [Fact]
    public void UnSoloCompradorSinPorcentaje_NoError()
    {
        var fila = Fila(("comprador_1_numero_documento", "1"), ("vendedor_1_numero_documento", "9"));

        BulkTramitesPercentageValidator.Validate(fila).Should().BeNull();
    }

    [Fact]
    public void UnSoloCompradorConPorcentajeDeMas_SeIgnora_NoBloquea()
    {
        var fila = Fila(
            ("comprador_1_numero_documento", "1"),
            ("comprador_1_porcentaje", "60"),
            ("vendedor_1_numero_documento", "9"));

        BulkTramitesPercentageValidator.Validate(fila).Should().BeNull();
    }

    [Fact]
    public void DosCompradores_SumanExactamente100_NoError()
    {
        var fila = Fila(
            ("comprador_1_numero_documento", "1"), ("comprador_1_porcentaje", "60"),
            ("comprador_2_numero_documento", "2"), ("comprador_2_porcentaje", "40"),
            ("vendedor_1_numero_documento", "9"));

        BulkTramitesPercentageValidator.Validate(fila).Should().BeNull();
    }

    [Fact]
    public void DosCompradores_SumanDistintoDe100_PorcentajesNoSuman100()
    {
        var fila = Fila(
            ("comprador_1_numero_documento", "1"), ("comprador_1_porcentaje", "60"),
            ("comprador_2_numero_documento", "2"), ("comprador_2_porcentaje", "30"),
            ("vendedor_1_numero_documento", "9"));

        BulkTramitesPercentageValidator.Validate(fila).Should().Be(BulkTramitesPercentageValidator.PorcentajesNoSuman100);
    }

    [Fact]
    public void DosCompradores_UnoEnCero_PorcentajeEnCero()
    {
        var fila = Fila(
            ("comprador_1_numero_documento", "1"), ("comprador_1_porcentaje", "100"),
            ("comprador_2_numero_documento", "2"), ("comprador_2_porcentaje", "0"),
            ("vendedor_1_numero_documento", "9"));

        BulkTramitesPercentageValidator.Validate(fila).Should().Be(BulkTramitesPercentageValidator.PorcentajeEnCero);
    }

    [Fact]
    public void DosCompradores_SinPorcentaje_SeTrataComoCero_PorcentajeEnCero()
    {
        var fila = Fila(
            ("comprador_1_numero_documento", "1"), ("comprador_1_porcentaje", "100"),
            ("comprador_2_numero_documento", "2"),
            ("vendedor_1_numero_documento", "9"));

        BulkTramitesPercentageValidator.Validate(fila).Should().Be(BulkTramitesPercentageValidator.PorcentajeEnCero);
    }

    [Fact]
    public void ElLadoVendedorSeValidaIndependienteDelComprador()
    {
        var fila = Fila(
            ("comprador_1_numero_documento", "1"),
            ("vendedor_1_numero_documento", "8"), ("vendedor_1_porcentaje", "50"),
            ("vendedor_2_numero_documento", "9"), ("vendedor_2_porcentaje", "40"));

        BulkTramitesPercentageValidator.Validate(fila).Should().Be(BulkTramitesPercentageValidator.PorcentajesNoSuman100);
    }

    [Fact]
    public void CuatroCompradores_SumanExactamente100_NoError()
    {
        var fila = Fila(
            ("comprador_1_numero_documento", "1"), ("comprador_1_porcentaje", "25"),
            ("comprador_2_numero_documento", "2"), ("comprador_2_porcentaje", "25"),
            ("comprador_3_numero_documento", "3"), ("comprador_3_porcentaje", "25"),
            ("comprador_4_numero_documento", "4"), ("comprador_4_porcentaje", "25"),
            ("vendedor_1_numero_documento", "9"));

        BulkTramitesPercentageValidator.Validate(fila).Should().BeNull();
    }
}
