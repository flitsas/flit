using System.Text;
using Flit.Infrastructure.Documents.Branding;
using FluentAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Branding;

/// <summary>
/// Tests del módulo base de marca documental FLIT (HU #10855, Feature #10852).
/// Cubre los AC: (1) módulo disponible y generadores producen PDF, (2) tema aplicado
/// (Carta, márgenes, color de marca), (3) overlays no destructivos.
/// </summary>
public sealed class BrandingModuleTests
{
    static BrandingModuleTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static byte[] MinimalPdf() =>
        Document.Create(c => c.Page(p =>
        {
            p.Size(PageSizes.Letter);
            p.Margin(1, Unit.Centimetre);
            p.Content().Text("contenido");
        })).GeneratePdf();

    // ---- AC1 / AC2: fuentes y tema ------------------------------------------------------------

    [Fact]
    public void EnsureRegistered_IsIdempotent_AndSetsSupersetResolver()
    {
        FlitFonts.EnsureRegistered();
        FlitFonts.EnsureRegistered(); // segunda llamada: no debe lanzar ni duplicar

        GlobalFontSettings.FontResolver.Should().BeOfType<FlitFontResolver>();
    }

    [Fact]
    public void Theme_UsesLetterSize_MarginAndBrandColor()
    {
        FlitDocumentTheme.Page.Width.Should().Be(PageSizes.Letter.Width);
        FlitDocumentTheme.Page.Height.Should().Be(PageSizes.Letter.Height);
        FlitDocumentTheme.MarginCm.Should().Be(2.54f);
        FlitDocumentTheme.PrimaryBlue.Should().Be("#557EFF");
        // 2,54 cm == 1 pulgada == 72 pt.
        FlitDocumentTheme.Cm(2.54).Should().BeApproximately(72d, 0.001);
    }

    [Theory]
    [InlineData("Poppins", false, false)]
    [InlineData("Poppins", true, false)]
    [InlineData("Poppins Medium", false, false)]
    [InlineData("Arial", false, false)]  // familia del overlay del FUR → fallback DejaVu
    public void FontResolver_ResolvesFamily_AndReturnsFontBytes(string family, bool bold, bool italic)
    {
        var resolver = new FlitFontResolver();

        var info = resolver.ResolveTypeface(family, bold, italic);
        info.Should().NotBeNull();

        var bytes = resolver.GetFont(info.FaceName);
        bytes.Should().NotBeNullOrEmpty("toda cara resuelta debe tener una TrueType embebida detrás");
    }

    // ---- AC3: overlays no destructivos --------------------------------------------------------

    [Fact]
    public void ApplyDocumentName_KeepsPageCount_AndProducesPdf()
    {
        var input = MinimalPdf();

        var stamped = FlitPdfStamper.ApplyDocumentName(input, "Certificado de tradición");

        Encoding.ASCII.GetString(stamped, 0, 4).Should().Be("%PDF");
        PageCount(stamped).Should().Be(PageCount(input));
    }

    [Fact]
    public void ApplyDocumentName_EmptyName_ReturnsInputUnchanged()
    {
        var input = MinimalPdf();

        FlitPdfStamper.ApplyDocumentName(input, "   ").Should().BeSameAs(input);
    }

    [Fact]
    public void ApplyWatermark_ProducesPdf_WithoutThrowing()
    {
        var input = MinimalPdf();

        byte[]? stamped = null;
        var act = () => stamped = FlitPdfStamper.ApplyWatermark(input, "BORRADOR");

        act.Should().NotThrow();
        Encoding.ASCII.GetString(stamped!, 0, 4).Should().Be("%PDF");
        PageCount(stamped!).Should().Be(PageCount(input));
    }

    // ---- Bug (novedad 27, nov.) + ADR-0049: geometría del sello por perfil de estampado ----------
    //
    // Un intento previo cubrió la colisión reportada (el sello pisando la rejilla de "TIPO DE
    // CARROCERÍA" en la hoja 2 del FUR AUTOMOTOR) con un fondo opaco blanco. Se revirtió: renderizando
    // el PDF, el rectángulo blanco borraba la línea inferior de la rejilla y la base de las etiquetas
    // verticales (TRACTOCAMIÓN→"RACTO", VOLQUETA→"OLQUE", IMPROVISADO→"MPRO"), recortaba una muesca en
    // la banda negra del pie de los documentos con membrete, y asumía papel blanco (falso en un
    // adjunto escaneado).
    //
    // La solución (ADR-0049) es un margen inferior propio para el perfil `Formulario`
    // (DocNameBottomFormularioCm = 0,56cm) que cae dentro de la franja libre medida en TODAS las
    // plantillas FUR (hoja 1 y hoja 2, en los tres formatos AUTOMOTOR/MAQUINARIA/REMOLQUES):
    // intersección 576,0–609,2 pt (perfil de tinta a 288dpi, x ∈ [W-202, W-72]pt). El resto de
    // documentos (perfil `Default`) conserva el margen histórico de 1,2cm sin cambios.
    private const double FurFreeBandTop = 576.0;
    private const double FurFreeBandBottom = 609.2;
    private const double LetterheadFooterWhiteBottomPt = 32.9; // blanco del footer SVG (membrete-hoja-footer.svg)

    private const double AutomotorPage1Width = 792d; // hoja 1 y hoja 2 automotor comparten tamaño
    private const double AutomotorPage1Height = 612d;
    private const double MaquinariaRemolquesWidth = 1008d;
    private const double MaquinariaRemolquesHeight = 612d;
    private const double LetterheadWidth = 612d;
    private const double LetterheadHeight = 792d;

    /// <summary>
    /// Ejercita <see cref="FlitPdfStamper.ComputeStampGeometry"/> (el método real de producción, no
    /// una copia paralela) para una página del tamaño dado, con el margen inferior del perfil pasado.
    /// </summary>
    private static (double TextTop, double TextBottom) ComputeGeometry(
        double pageWidthPt, double pageHeightPt, float bottomCm, string documentName = "Certificado de tradición")
    {
        FlitFonts.EnsureRegistered();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont(FlitDocumentTheme.FontMedium, FlitDocumentTheme.DocNameFontSize, XFontStyle.Regular);
        var textSize = gfx.MeasureString(documentName, font);

        var rightPt = FlitDocumentTheme.Cm(FlitDocumentTheme.DocNameRightCm);
        var bottomPt = FlitDocumentTheme.Cm(bottomCm);

        var geometry = FlitPdfStamper.ComputeStampGeometry(pageWidthPt, pageHeightPt, textSize, rightPt, bottomPt);

        var ascentPt = font.Metrics.Ascent * font.Size / font.Metrics.UnitsPerEm;
        var descentPt = font.Metrics.Descent * font.Size / font.Metrics.UnitsPerEm;
        var textTop = geometry.BaselineY - ascentPt;
        var textBottom = geometry.BaselineY + descentPt;

        return (textTop, textBottom);
    }

    [Theory]
    [InlineData(AutomotorPage1Width, AutomotorPage1Height)]  // hoja 1 y hoja 2 automotor comparten tamaño (792×612)
    [InlineData(MaquinariaRemolquesWidth, MaquinariaRemolquesHeight)]
    public void ApplyDocumentName_FormularioProfile_FallsWithinFurFreeBand(double width, double height)
    {
        var (top, bottom) = ComputeGeometry(width, height, FlitDocumentTheme.DocNameBottomFormularioCm);

        top.Should().BeGreaterThanOrEqualTo(FurFreeBandTop,
            "el perfil Formulario (0,56cm) debe caer dentro de la franja libre de TODAS las plantillas FUR");
        bottom.Should().BeLessThanOrEqualTo(FurFreeBandBottom,
            "el perfil Formulario (0,56cm) no debe acercarse tanto al borde que se salga de la hoja");
        bottom.Should().BeGreaterThanOrEqualTo(0, "el texto no puede quedar fuera de la página");
    }

    [Fact]
    public void ApplyDocumentName_DefaultProfile_OnLetterheadDocuments_StaysAboveFooterWhiteBand()
    {
        // Los documentos con membrete (mandato/solicitud/compraventa, tamaño Carta) NO se movieron:
        // el perfil Default (1,2cm) sigue por encima de la banda negra sólida del footer SVG. La
        // franja libre (32,9pt) se mide como DISTANCIA AL BORDE FÍSICO INFERIOR de la hoja — no como
        // la coordenada top-down que devuelve ComputeStampGeometry — así que hay que invertirla.
        var (_, textBottomTopDown) = ComputeGeometry(LetterheadWidth, LetterheadHeight, FlitDocumentTheme.DocNameBottomCm);
        var distanceFromPageBottomEdge = LetterheadHeight - textBottomTopDown;

        distanceFromPageBottomEdge.Should().BeLessThanOrEqualTo(LetterheadFooterWhiteBottomPt,
            "el perfil Default no cambió: el sello del pie sigue dentro del blanco del footer SVG del membrete");
    }

    // ---- AC1: generador de portada ------------------------------------------------------------

    [Fact]
    public void CoverGenerator_ProducesLetterPdf()
    {
        var data = new FlitCoverData("TRM-2026-000123", "ABC123", "Traspaso", "Secretaría de Tránsito de Medellín", "FLIT SAS");

        var pdf = FlitCoverPageGenerator.Generate(data);

        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
        PageCount(pdf).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CoverGenerator_WithEmptyValues_DoesNotThrow()
    {
        var data = new FlitCoverData("", "", "", "", "");

        byte[]? pdf = null;
        var act = () => pdf = FlitCoverPageGenerator.Generate(data);

        act.Should().NotThrow();
        Encoding.ASCII.GetString(pdf!, 0, 4).Should().Be("%PDF");
    }

    private static int PageCount(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }
}
