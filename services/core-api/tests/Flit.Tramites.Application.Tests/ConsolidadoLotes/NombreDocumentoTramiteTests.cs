using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>HU #14734 (Feature #14733, Épica #14657) — nombre de documentos <c>{tipo}_{radicado}_{placa}.pdf</c>.</summary>
public class NombreDocumentoTramiteTests
{
    [Fact]
    public void ConPlaca_GeneraNombreCompleto() =>
        NombreDocumentoTramite.Generar("consolidado_maestro", "FT1-0000101", "AAA111")
            .Should().Be("consolidado_maestro_FT1-0000101_AAA111.pdf");

    [Fact]
    public void SinPlaca_UsaSinPlaca() =>
        NombreDocumentoTramite.Generar("fur", "FT1-0000102", null)
            .Should().Be("fur_FT1-0000102_SIN-PLACA.pdf");

    [Fact]
    public void PlacaConGuionEspacioYMinusculas_SeSanea() =>
        NombreDocumentoTramite.Generar("fur", "FT1-1", "ab-12 3")
            .Should().Be("fur_FT1-1_AB123.pdf");

    [Fact]
    public void PlacaSoloSimbolos_UsaSinPlaca() =>
        NombreDocumentoTramite.Generar("fur", "FT1-1", "--//")
            .Should().Be("fur_FT1-1_SIN-PLACA.pdf");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../\\")]
    public void RadicadoVacioOSoloPeligroso_UsaSinRadicado(string? radicado) =>
        NombreDocumentoTramite.Generar("fur", radicado, "AAA111")
            .Should().Be("fur_SIN-RADICADO_AAA111.pdf");

    [Fact]
    public void RadicadoConCaracteresPeligrosos_NoPuedeEscaparDeLaCarpeta()
    {
        var nombre = NombreDocumentoTramite.Generar("fur", "../ft1-9\\x", "AAA111");

        nombre.Should().Be("fur_FT1-9X_AAA111.pdf");
        nombre.Should().NotContainAny("/", "\\", "..");
    }

    [Fact]
    public void TipoPeligroso_SeSaneaAMinusculasSinSalirDeLaCarpeta()
    {
        var nombre = NombreDocumentoTramite.Generar("../Fur Maestro\\", "FT1-1", "AAA111");

        nombre.Should().Be("furmaestro_FT1-1_AAA111.pdf");
        nombre.Should().NotContainAny("/", "\\", "..");
    }

    [Fact]
    public void TipoVacio_UsaTipoPorDefecto() =>
        NombreDocumentoTramite.Generar("", "FT1-1", "AAA111")
            .Should().Be("documento_FT1-1_AAA111.pdf");
}
