using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13377 (AC4) — <see cref="ConsolidadoLoteNombres"/>: nombre del ZIP con hora de Colombia y
/// <c>parte-kk-de-tt</c>, nombre del PDF con la placa saneada a <c>[A-Z0-9]</c> o <c>SIN-PLACA</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// ConsolidadoLoteNombres.Zip(creadoEn, 2, 4); // consolidados_20261006_1430_parte-02-de-04.zip
/// </code>
/// </remarks>
public sealed class ConsolidadoLoteNombresTests
{
    /// <summary>2026-10-06 14:30 en Colombia = 19:30 UTC.</summary>
    private static readonly DateTimeOffset CreadoEnUtc = new(2026, 10, 6, 19, 30, 45, TimeSpan.Zero);

    [Fact]
    public void AC4_ParteUnica_SinSufijoDeParte_ConHoraDeColombia()
    {
        ConsolidadoLoteNombres.Zip(CreadoEnUtc, 1, 1).Should().Be("consolidados_20261006_1430.zip");
    }

    [Fact]
    public void AC4_CuatroPartes_LaSegunda_LlevaParte02De04()
    {
        ConsolidadoLoteNombres.Zip(CreadoEnUtc, 2, 4).Should().Be("consolidados_20261006_1430_parte-02-de-04.zip");
    }

    [Fact]
    public void AC4_HoraDeColombia_CambiaDeDia_RespectoAUtc()
    {
        // 2026-10-07 03:10 UTC = 2026-10-06 22:10 en Colombia.
        var utc = new DateTimeOffset(2026, 10, 7, 3, 10, 0, TimeSpan.Zero);

        ConsolidadoLoteNombres.Zip(utc, 1, 1).Should().Be("consolidados_20261006_2210.zip");
        ConsolidadoLoteNombres.Zip(utc.ToOffset(TimeSpan.FromHours(2)), 1, 1)
            .Should().Be("consolidados_20261006_2210.zip", "el offset de entrada no cambia el resultado");
    }

    [Fact]
    public void AC4_MasDe99Partes_NoTrunca()
    {
        ConsolidadoLoteNombres.Zip(CreadoEnUtc, 7, 120).Should().Be("consolidados_20261006_1430_parte-07-de-120.zip");
    }

    [Theory]
    [InlineData("R-2026-000123", "ABC123", "R-2026-000123_ABC123.pdf")]
    [InlineData("R-2026-000123", "abc-12d", "R-2026-000123_ABC12D.pdf")]
    [InlineData("R-2026-000123", " =AB C/1..2 ", "R-2026-000123_ABC12.pdf")]
    [InlineData("R-2026-000123", null, "R-2026-000123_SIN-PLACA.pdf")]
    [InlineData("R-2026-000123", "", "R-2026-000123_SIN-PLACA.pdf")]
    [InlineData("R-2026-000123", "--//", "R-2026-000123_SIN-PLACA.pdf")]
    [InlineData("R-2026-000123", "ÑAÑ12", "R-2026-000123_A12.pdf")]
    public void AC4_Pdf_RadicadoYPlacaSaneada_OSinPlaca(string radicado, string? placa, string esperado)
    {
        ConsolidadoLoteNombres.Pdf(radicado, placa).Should().Be(esperado);
    }

    [Theory]
    [InlineData("../../etc/passwd", "ETCPASSWD")]
    [InlineData("r-1\\..\\x", "R-1X")]
    [InlineData("", "SIN-RADICADO")]
    [InlineData(null, "SIN-RADICADO")]
    public void AC4_Negativo_ElRadicadoNoPuedeSalirDelZip(string? radicado, string prefijo)
    {
        var nombre = ConsolidadoLoteNombres.Pdf(radicado, "ABC123");

        nombre.Should().Be($"{prefijo}_ABC123.pdf");
        nombre.Should().NotContainAny("/", "\\", "..");
    }

    [Fact]
    public void Contrato_Constantes()
    {
        ConsolidadoLoteNombres.OmitidosCsv.Should().Be("omitidos.csv");
        ConsolidadoLoteNombres.SanearPlaca("xyz-9 8").Should().Be("XYZ98");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    public void Contrato_ParteFueraDeRango_Lanza(int parte, int total)
    {
        var act = () => ConsolidadoLoteNombres.Zip(CreadoEnUtc, parte, total);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
