using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>
/// Estampa overlays sobre un PDF ya renderizado, con PdfSharpCore (HU #10855):
/// <list type="bullet">
///   <item><see cref="ApplyDocumentName"/>: nombre del documento en el pie de cada página
///     (Poppins Medium 8pt, #557EFF, a 2,54 cm del borde derecho y 1,2 cm del inferior). Sirve tanto
///     para documentos generados por FLIT como para adjuntos del usuario, en cualquier tamaño de hoja.</item>
///   <item><see cref="ApplyWatermark"/>: marca de agua diagonal con el estado del trámite.</item>
/// </list>
/// El nombre del documento se dibuja dentro del margen inferior (1,2 cm &lt; 2,54 cm), por lo que
/// debe estamparse por overlay y no puede componerse dentro del área de contenido de QuestPDF.
/// </summary>
public static class FlitPdfStamper
{
    private static readonly XColor DocNameColor = XColor.FromArgb(0x55, 0x7E, 0xFF);
    private static readonly XColor WatermarkColor = XColor.FromArgb(38, 0x16, 0x27, 0x44);

    /// <summary>Dibuja el nombre del documento en el pie de cada página. Devuelve el PDF resultante.</summary>
    public static byte[] ApplyDocumentName(byte[] pdf, string documentName)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (string.IsNullOrWhiteSpace(documentName))
            return pdf;

        FlitFonts.EnsureRegistered();
        using var document = Open(pdf);
        var font = new XFont(FlitDocumentTheme.FontMedium, FlitDocumentTheme.DocNameFontSize, XFontStyle.Regular);
        var brush = new XSolidBrush(DocNameColor);
        var rightPt = FlitDocumentTheme.Cm(FlitDocumentTheme.DocNameRightCm);
        var bottomPt = FlitDocumentTheme.Cm(FlitDocumentTheme.DocNameBottomCm);
        var text = documentName.Trim();

        for (var i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page);
            var size = gfx.MeasureString(text, font);
            var x = page.Width.Point - rightPt - size.Width;
            var y = page.Height.Point - bottomPt; // línea base del texto
            gfx.DrawString(text, font, brush, new XPoint(Math.Max(0, x), y));
        }

        return Save(document);
    }

    /// <summary>Estampa una marca de agua diagonal (estado) centrada en cada página. Devuelve el PDF resultante.</summary>
    public static byte[] ApplyWatermark(byte[] pdf, string statusLabel)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (string.IsNullOrWhiteSpace(statusLabel))
            return pdf;

        FlitFonts.EnsureRegistered();
        using var document = Open(pdf);
        var text = statusLabel.Trim().ToUpperInvariant();
        var brush = new XSolidBrush(WatermarkColor);

        for (var i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            var width = page.Width.Point;
            var height = page.Height.Point;
            using var gfx = XGraphics.FromPdfPage(page);

            var fontSize = Math.Clamp(width / Math.Max(text.Length, 6) * 1.6, 32, 120);
            var font = new XFont(FlitDocumentTheme.FontRegular, fontSize, XFontStyle.Bold);
            var size = gfx.MeasureString(text, font);

            var state = gfx.Save();
            gfx.TranslateTransform(width / 2, height / 2);
            gfx.RotateTransform(-45);
            gfx.DrawString(text, font, brush, new XPoint(-size.Width / 2, size.Height / 4));
            gfx.Restore(state);
        }

        return Save(document);
    }

    private static PdfDocument Open(byte[] pdf)
    {
        using var input = new MemoryStream(pdf);
        return PdfReader.Open(input, PdfDocumentOpenMode.Modify);
    }

    private static byte[] Save(PdfDocument document)
    {
        using var ms = new MemoryStream();
        document.Save(ms, false);
        return ms.ToArray();
    }
}
