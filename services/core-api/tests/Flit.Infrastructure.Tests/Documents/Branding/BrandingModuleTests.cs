using System.Text;
using Flit.Infrastructure.Documents.Branding;
using FluentAssertions;
using PdfSharpCore.Fonts;
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
