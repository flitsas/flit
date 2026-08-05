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

/// <summary>
/// HU #11256 — encaje del texto en la caja declarada de un campo <c>multiline</c> con
/// <c>autoFit: true</c> (hoy, solo <c>observations</c>). Geometría del caso real (automotor):
/// caja 403.1 × 33.0 pt, cuerpo base 7.2. La medición se inyecta igual que en <see cref="FurTextFitterTests"/>
/// para que el algoritmo se pruebe sin PdfSharpCore ni fuentes del sistema.
/// </summary>
public sealed class FurTextFitterFitMultilineTests
{
    /// <summary>Ancho aproximado de carácter: 0,5 × cuerpo (igual criterio que <see cref="FurTextFitterTests"/>).</summary>
    private static double Measure(string text, double fontSize) => text.Length * fontSize * 0.5;

    private const double CampoW = 403.1;
    private const double CampoH = 33.0;
    private const double CampoFont = 7.2;

    private static FurTextFit FitMultiline(
        string text, double w = CampoW, double h = CampoH, double font = CampoFont, Action<int>? onTruncate = null) =>
        FurTextFitter.FitMultiline(text, w, h, font, Measure, onTruncate);

    [Fact]
    public void TextoQueYaCabe_PassthroughExacto_MismasLineasYMismoCuerpo()
    {
        // 30 chars × 7.2 × 0.5 = 108 pt < 403.1 ⇒ una sola línea, cuerpo intacto (garantía CF4).
        const string texto = "Vehículo con platón adaptado.";
        var fit = FitMultiline(texto);

        fit.Lines.Should().Equal(texto);
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void TextoVacio_PassthroughDevuelveSinLineas()
    {
        var fit = FitMultiline(string.Empty);

        fit.Lines.Should().BeEmpty();
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void AnchoCero_NoAlteraElTexto()
    {
        var fit = FitMultiline("CUALQUIER OBSERVACIÓN MUY LARGA QUE NO SE MIDE PORQUE EL CAMPO NO DECLARA ANCHO", w: 0);

        fit.Lines.Should().Equal(["CUALQUIER OBSERVACIÓN MUY LARGA QUE NO SE MIDE PORQUE EL CAMPO NO DECLARA ANCHO"]);
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void SaltosDeLineaExplicitos_SeRespetanComoParrafosSeparados()
    {
        // Dos párrafos cortos que caben cada uno en el ancho y cuyo bloque cabe en el alto: passthrough,
        // preservando el salto duro como dos líneas (no se concatenan en una).
        var fit = FitMultiline("Primera línea corta.\nSegunda línea corta.");

        fit.Lines.Should().Equal("Primera línea corta.", "Segunda línea corta.");
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void ParrafoLargo_SeEnvuelveAlCuerpoBase_SinReducirElCuerpo()
    {
        // Un párrafo que no cabe en una línea al cuerpo base pero cuyo envolvido sí cabe en el alto de 3
        // líneas (CampoH=33 ⇒ MaxLines(7.2)=floor(33/9)=3).
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 20)); // 20×"PALABRA " ≈ 160 chars

        var fit = FitMultiline(texto);

        fit.FontSize.Should().Be(CampoFont);
        fit.Lines.Count.Should().BeGreaterThan(1).And.BeLessThanOrEqualTo(3);
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
    }

    [Fact]
    public void ParrafoMuyLargo_ReduceElCuerpoReenvolviendo()
    {
        // Demasiadas palabras para caber en 3 líneas al cuerpo base: baja de cuerpo re-envolviendo hasta
        // que el número de líneas quepa en el alto.
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 60));

        var fit = FitMultiline(texto);

        fit.FontSize.Should().BeLessThan(CampoFont).And.BeGreaterThanOrEqualTo(FurTextFitterFitMultilineTests.MinMultilineFontSizeForTests);
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(CampoH);
    }

    /// <summary>Espejo de <c>FurTextFitter.MinMultilineFontSize</c> (privado): el piso documentado en el diseño.</summary>
    private const double MinMultilineFontSizeForTests = 5;

    [Fact]
    public void TextoDesmedido_NuncaBajaDelPisoDe5Puntos()
    {
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 400)); // ≈2.800 caracteres

        var fit = FitMultiline(texto);

        fit.FontSize.Should().Be(MinMultilineFontSizeForTests);
    }

    [Fact]
    public void TextoDesmedido_TruncaConElipsisYAvisaCaracteresElididos()
    {
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 400));
        int? elidedChars = null;

        var fit = FitMultiline(texto, onTruncate: n => elidedChars = n);

        fit.Lines[^1].Should().EndWith("…");
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(CampoH);
        elidedChars.Should().NotBeNull().And.BeGreaterThan(0);
    }

    [Fact]
    public void PalabraUnicaMasAnchaQueLaCaja_NuncaSeDibujaFueraDeLaCaja()
    {
        // Una sola "palabra" (sin espacios) tan larga que ningún cuerpo entre el base y el piso la hace
        // caber: debe terminar truncada, nunca desbordando el ancho declarado.
        var fit = FitMultiline(new string('X', 400));

        fit.Lines.Should().HaveCount(1);
        fit.Lines[0].Should().EndWith("…");
        Measure(fit.Lines[0], fit.FontSize).Should().BeLessThanOrEqualTo(CampoW);
    }

    [Fact]
    public void CabeConVariasLineasCortas_SinLlegarAlLimiteDeAlto_QuedaAlCuerpoBase()
    {
        // Tres párrafos con `\n` explícitos, cada uno corto: caben tal cual al cuerpo base porque tanto
        // el ancho por párrafo como el alto total (3 líneas) respetan la caja.
        var fit = FitMultiline("Línea uno.\nLínea dos.\nLínea tres.");

        fit.Lines.Should().Equal("Línea uno.", "Línea dos.", "Línea tres.");
        fit.FontSize.Should().Be(CampoFont);
    }
}
