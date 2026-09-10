using System.Text.RegularExpressions;
using Flit.Infrastructure.Documents.Fur;
using FluentAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Casilla 20 del FUR, columna "A FAVOR DE" (<c>alert_data_code_5</c>): el acreedor de la prenda,
/// medido con la fuente REAL igual que <see cref="FurCasilla19FitTests"/>.
///
/// <para><b>Qué la hace distinta de las demás.</b> Es la caja más estrecha del formulario en la que
/// se escribe un nombre propio: ~50 pt de ancho útil (la columna del blank mide 54) contra los 130 de
/// la casilla 19. Un banco no cabe de ninguna manera en un renglón, así que el campo se declara
/// <c>multiline</c> con auto-encaje y crece HACIA ABAJO, que es el único sitio donde hay aire: bajo
/// el rótulo impreso quedan ~24 pt libres hasta el borde inferior de la sección.</para>
///
/// <para>Los valores se leen del manifiesto, nunca se copian: si alguien ensancha la caja o sube el
/// cuerpo hasta que el acreedor deje de caber, estos tests lo dicen.</para>
/// </summary>
public sealed class FurCasilla20FitTests
{
    /// <summary>El acreedor de los escenarios de prenda de <c>tools/fur-preview</c>.</summary>
    private const string AcreedorTipico = "BANCO FINANCIERO DE COLOMBIA S.A.";

    /// <summary>
    /// El peor caso conocido de razón social bancaria: 79 caracteres, la que el RUES devuelve para el
    /// NIT 890903938. Aquí no se exige que salga íntegra —a este ancho sería físicamente ilegible—,
    /// sino que la tinta no se salga de la columna: un acreedor desbordado pisa la casilla vecina y
    /// deja el formulario inservible.
    /// </summary>
    private const string AcreedorDesmedido =
        "BANCOLOMBIA S.A, ADEMÁS PODRÁ GIRAR BAJO LA DENOMINACIÓN BANCO DE COLOMBIA S.A.";

    [Fact]
    public void AcreedorTipico_SaleEnteroYEnVariasLineas()
    {
        var (fit, campo) = Encajar(AcreedorTipico);

        NormalizarEspacios(string.Join(" ", fit.Lines)).Should().Be(
            NormalizarEspacios(AcreedorTipico),
            "el nombre del acreedor es el dato que la casilla 20 existe para declarar: si se recorta, " +
            "el gravamen queda a favor de nadie");
        fit.Lines.Should().NotContain(l => l.Contains('…'));
        fit.Lines.Count.Should().BeGreaterThan(1, "en 50 pt de ancho ningún banco cabe en un renglón");
        fit.FontSize.Should().Be(campo.FontSize, "el caso corriente no debería necesitar encogerse");
    }

    [Theory]
    [InlineData(AcreedorTipico)]
    [InlineData(AcreedorDesmedido)]
    public void Acreedor_NoSeSaleDeLaColumna(string acreedor)
    {
        var (fit, campo) = Encajar(acreedor);

        using var lienzo = NuevoLienzo();
        foreach (var linea in fit.Lines)
            Ancho(lienzo.Gfx, linea, fit.FontSize).Should().BeLessThanOrEqualTo(campo.W,
                "una línea más ancha que la columna se estampa sobre la casilla vecina");

        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(campo.H,
            "por debajo del borde inferior de la sección 20 empieza el numeral 23");
        fit.FontSize.Should().BeGreaterThanOrEqualTo(campo.MinFontSize!.Value);
    }

    // ── Andamiaje ─────────────────────────────────────────────────────────────

    private static (FurTextFit Fit, FurFieldDefinition Campo) Encajar(string texto)
    {
        var campo = Campo();
        using var lienzo = NuevoLienzo();

        var fit = FurTextFitter.FitMultiline(
            texto,
            campo.W,
            campo.H,
            campo.FontSize,
            (valor, cuerpo) => Ancho(lienzo.Gfx, valor, cuerpo),
            _ => { },
            campo.MinFontSize);

        return (fit, campo);
    }

    private static FurFieldDefinition Campo() =>
        FurFieldManifestLoader.LoadEmbedded().Fields.Single(f => f.Id == "alert_data_code_5");

    private static double Ancho(XGraphics gfx, string texto, double cuerpo) =>
        gfx.MeasureString(texto, new XFont("Arial", cuerpo, XFontStyle.Bold)).Width;

    private static Lienzo NuevoLienzo()
    {
        FurFontResolver.EnsureRegistered();
        return new Lienzo();
    }

    private sealed class Lienzo : IDisposable
    {
        private readonly PdfDocument _doc = new();

        public Lienzo() => Gfx = XGraphics.FromPdfPage(_doc.AddPage());

        public XGraphics Gfx { get; }

        public void Dispose()
        {
            Gfx.Dispose();
            _doc.Dispose();
        }
    }

    private static string NormalizarEspacios(string valor) => Regex.Replace(valor.Trim(), @"\s+", " ");
}
