using Flit.Infrastructure.Documents.Fur;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #11048 — encaje del texto en la caja declarada del campo del FUR. Se usa una medición lineal
/// inyectada (ancho = caracteres × cuerpo × factor) para que el algoritmo se pruebe sin PdfSharpCore ni
/// fuentes del sistema. Geometría real del caso reportado: el campo de nombre del propietario declara
/// <c>w = 93.5</c>, <c>h = 14.4</c> y cuerpo <c>7.7</c>.
/// </summary>
public sealed class FurTextFitterTests
{
    /// <summary>Ancho aproximado de carácter: 0,5 × cuerpo (parecido a una sans a ojo).</summary>
    private static double Measure(string text, double fontSize) => text.Length * fontSize * 0.5;

    private const double CampoW = 93.5;
    private const double CampoH = 14.4;
    private const double CampoFont = 7.7;

    private static FurTextFit Fit(string text, double w = CampoW, double h = CampoH, double font = CampoFont) =>
        FurTextFitter.Fit(text, w, h, font, Measure);

    [Fact]
    public void TextoQueYaCabe_NoSeToca()
    {
        // 12 chars × 7.7 × 0.5 = 46.2 pt < 93.5 ⇒ intacto, con el cuerpo calibrado del manifiesto.
        var fit = Fit("Juan Pérez A");

        fit.Lines.Should().Equal("Juan Pérez A");
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void TextoAlgoMasLargo_ReduceElCuerpoYSigueEnUnaLinea()
    {
        // 28 chars: no cabe a 7.7 (107.8) pero sí a 6.5 (91) ⇒ una línea con cuerpo menor.
        var fit = Fit(new string('A', 28));

        fit.Lines.Should().HaveCount(1);
        fit.FontSize.Should().BeLessThan(CampoFont).And.BeGreaterThanOrEqualTo(CampoFont * 0.65);
        Measure(fit.Lines[0], fit.FontSize).Should().BeLessThanOrEqualTo(CampoW);
    }

    [Fact]
    public void RazonSocialLarga_SeParteEnVariasLineasDentroDelAlto()
    {
        var fit = Fit("COMERCIALIZADORA INTERNACIONAL DE VEHICULOS Y MAQUINARIA S.A.S");

        fit.Lines.Count.Should().BeGreaterThan(1);
        // Todas las líneas caben en el ancho…
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        // …y el bloque cabe en el alto del campo.
        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(CampoH);
    }

    [Fact]
    public void NuncaBajaDelMinimoLegible()
    {
        var fit = Fit(new string('X', 400));

        fit.FontSize.Should().BeGreaterThanOrEqualTo(Math.Max(3, CampoFont * 0.65));
    }

    [Fact]
    public void CuandoNadaCabe_TruncaConElipsisEnUnaSolaLinea()
    {
        // Una única palabra larguísima no se puede partir por palabras: se trunca.
        var fit = Fit(new string('X', 400));

        fit.Lines.Should().HaveCount(1);
        fit.Lines[0].Should().EndWith("…");
        Measure(fit.Lines[0], fit.FontSize).Should().BeLessThanOrEqualTo(CampoW);
    }

    [Fact]
    public void CampoSinAnchoDeclarado_NoAlteraElTexto()
    {
        var fit = Fit("CUALQUIER COSA MUY LARGA QUE NO SE MIDE", w: 0);

        fit.Lines.Should().Equal("CUALQUIER COSA MUY LARGA QUE NO SE MIDE");
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void CampoDeUnaSolaLinea_NoParteAunqueElTextoSeaLargo()
    {
        // Alto justo para una línea a cualquier cuerpo admisible ⇒ no hay wrap posible.
        var fit = Fit("EMPRESA DE TRANSPORTES Y LOGISTICA NACIONAL", h: 6);

        fit.Lines.Should().HaveCount(1);
    }

    // El caso concreto que reportó el negocio.
    [Fact]
    public void BancolombiaSas_CabeDentroDelCampo()
    {
        var fit = Fit("BANCOLOMBIA S.A.S");

        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(CampoH);
    }
}
