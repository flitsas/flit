using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>
/// Estampa overlays sobre un PDF ya renderizado, con PdfSharpCore (HU #10855):
/// <list type="bullet">
///   <item><see cref="ApplyDocumentName"/>: nombre del documento en el pie de cada página
///     (Poppins Medium 8pt, #557EFF, a 2,54 cm del borde derecho). El margen inferior depende del
///     perfil de estampado del documento (ver <see cref="FlitDocumentTheme.DocNameBottomCm"/> y
///     <see cref="FlitDocumentTheme.DocNameBottomFormularioCm"/>, ADR-0049): sirve tanto para
///     documentos generados por FLIT como para adjuntos del usuario, en cualquier tamaño de hoja,
///     pero para estos últimos es <b>best effort sin garantía</b> — FLIT no conoce el contenido bajo
///     el sello de un adjunto escaneado por el usuario, así que no puede afirmar que nunca colisione
///     con algo debajo.</item>
///   <item><see cref="ApplyWatermark"/>: marca de agua diagonal con el estado del trámite.</item>
/// </list>
/// El nombre del documento se dibuja dentro del margen inferior, por lo que debe estamparse por
/// overlay y no puede componerse dentro del área de contenido de QuestPDF.
/// </summary>
public static class FlitPdfStamper
{
    private static readonly XColor DocNameColor = XColor.FromArgb(0x55, 0x7E, 0xFF);
    private static readonly XColor WatermarkColor = XColor.FromArgb(38, 0x16, 0x27, 0x44);

    /// <summary>
    /// Dibuja el nombre del documento en el pie de cada página. Devuelve el PDF resultante.
    /// <paramref name="bottomCm"/> es <b>best effort sin garantía</b> sobre adjuntos del usuario:
    /// FLIT no conoce el contenido bajo el sello (una rejilla, una firma, una imagen escaneada), así
    /// que solo puede evitar colisiones conocidas con documentos que FLIT mismo genera (ver
    /// ADR-0049).
    /// </summary>
    public static byte[] ApplyDocumentName(byte[] pdf, string documentName, float bottomCm = FlitDocumentTheme.DocNameBottomCm)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (string.IsNullOrWhiteSpace(documentName))
            return pdf;

        FlitFonts.EnsureRegistered();
        using var document = Open(pdf);
        var font = new XFont(FlitDocumentTheme.FontMedium, FlitDocumentTheme.DocNameFontSize, XFontStyle.Regular);
        var brush = new XSolidBrush(DocNameColor);
        var rightPt = FlitDocumentTheme.Cm(FlitDocumentTheme.DocNameRightCm);
        var bottomPt = FlitDocumentTheme.Cm(bottomCm);
        var text = documentName.Trim();

        for (var i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page);
            var size = gfx.MeasureString(text, font);
            var geometry = ComputeStampGeometry(page.Width.Point, page.Height.Point, size, rightPt, bottomPt);

            gfx.DrawString(text, font, brush, new XPoint(geometry.TextX, geometry.BaselineY));
        }

        return Save(document);
    }

    /// <summary>
    /// Calcula dónde cae la línea base del sello (texto) para una página dada. Aislado del bucle de
    /// <see cref="ApplyDocumentName"/> para que las pruebas de geometría por perfil (ADR-0049)
    /// ejerciten EXACTAMENTE esta cuenta — no una copia paralela en el proyecto de tests
    /// (<c>InternalsVisibleTo</c> hacia <c>Flit.Infrastructure.Tests</c>/<c>Flit.Admin.Tests</c>).
    /// </summary>
    internal static (double TextX, double BaselineY) ComputeStampGeometry(
        double pageWidthPt, double pageHeightPt, XSize textSize, double rightPt, double bottomPt)
    {
        var x = Math.Max(0, pageWidthPt - rightPt - textSize.Width);
        var y = pageHeightPt - bottomPt; // línea base del texto

        return (x, y);
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
