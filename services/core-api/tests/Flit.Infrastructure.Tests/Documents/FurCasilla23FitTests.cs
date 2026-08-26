using Flit.Infrastructure.Documents.Fur;
using FluentAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Casilla 23 del FUR ("OBSERVACIONES" → <c>observations</c>) medida con la fuente REAL, igual que
/// <see cref="FurCasilla19FitTests"/> y por el mismo motivo: la medición sintética prueba el
/// algoritmo, no si ESTE texto cabe en ESTA caja.
///
/// <para><b>Lo que estos tests fijan.</b> El recuadro se llenaba hasta el filo: con el ancho
/// declarado en 403,1 la primera línea del caso real medía 396,1 pt y el auto-encaje la daba por
/// buena —cabía, según el manifiesto—, pero al imprimir el texto tocaba la línea vertical del
/// recuadro. El auto-encaje no fallaba: no tenía margen que respetar. Se recalibró el campo con aire
/// (392 pt) y un cuerpo menor (6,5), y estos tests impiden que se pierda: cualquier línea que se
/// acerque al borde vuelve a fallar aquí en vez de descubrirse en un FUR impreso.</para>
///
/// <para>Los valores se leen DEL MANIFIESTO, no se copian.</para>
/// </summary>
public sealed class FurCasilla23FitTests
{
    /// <summary>
    /// El caso reportado: una transformación declarada más el bloque de servicio + vinculadora, que
    /// es la combinación más larga que produce hoy el texto automático.
    /// </summary>
    private const string ObservacionesReales =
        "Cambio de color: ABANO BLANCO. Servicio: PÚBLICO. Empresa vinculadora: BANCOLOMBIA S.A.S, NIT 890903938.";

    [Fact]
    public void ObservacionesReales_NingunaLineaTocaElBorde()
    {
        var (fit, campo, gfx, lienzo) = Encajar(ObservacionesReales);
        using (lienzo)
        {
            foreach (var linea in fit.Lines)
                Ancho(gfx, linea, fit.FontSize, campo.Bold).Should().BeLessThanOrEqualTo(campo.W);
        }
    }

    [Fact]
    public void ObservacionesReales_CabenEnElAltoDeclarado()
    {
        var (fit, campo, _, lienzo) = Encajar(ObservacionesReales);
        using (lienzo)
        {
            (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(campo.H);
        }
    }

    /// <summary>El texto sale ENTERO: la casilla tiene alto de sobra, truncar aquí sería gratuito.</summary>
    [Fact]
    public void ObservacionesReales_NoSeTruncan()
    {
        var (fit, _, _, lienzo) = Encajar(ObservacionesReales);
        using (lienzo)
        {
            string.Join(" ", fit.Lines).Should().Be(ObservacionesReales);
            fit.Lines.Should().NotContain(l => l.Contains('…'));
        }
    }

    /// <summary>
    /// El aire del recuadro es la razón de la recalibración, así que se afirma: el campo declara
    /// menos ancho del que la caja admite, para que el texto nunca llegue a la línea vertical.
    /// </summary>
    [Fact]
    public void ElCampoConservaMargenFrenteAlBordeDibujado()
    {
        var campo = Campo();

        campo.W.Should().BeLessThan(
            403.1, "403,1 era el ancho a filo de borde con el que el texto tocaba el recuadro");
        campo.FontSize.Should().BeLessThanOrEqualTo(
            6.5, "el cuerpo bajó para que el caso real quepa holgado en sus renglones");
    }

    // ── Andamiaje ─────────────────────────────────────────────────────────────

    private static (FurTextFit Fit, FurFieldDefinition Campo, XGraphics Gfx, IDisposable Lienzo) Encajar(string texto)
    {
        var campo = Campo();
        FurFontResolver.EnsureRegistered();
        var lienzo = new Lienzo();

        var fit = FurTextFitter.FitMultiline(
            texto,
            campo.W,
            campo.H,
            campo.FontSize,
            (valor, cuerpo) => Ancho(lienzo.Gfx, valor, cuerpo, campo.Bold),
            null,
            campo.MinFontSize);

        return (fit, campo, lienzo.Gfx, lienzo);
    }

    private static FurFieldDefinition Campo() =>
        FurFieldManifestLoader.LoadEmbedded().Fields.Single(f => f.Id == "observations");

    /// <summary>Misma medición que el renderer (<c>FurOverlayRenderer.DrawText</c>).</summary>
    private static double Ancho(XGraphics gfx, string texto, double cuerpo, bool bold) =>
        gfx.MeasureString(texto, new XFont("Arial", cuerpo, bold ? XFontStyle.Bold : XFontStyle.Regular)).Width;

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
}
