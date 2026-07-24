using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>Superpone valores sobre plantillas PDF blank con PdfSharpCore.</summary>
public static class FurOverlayRenderer
{
    /// <summary>Ancho máximo de la imagen de firma del baúl dentro del campo (el resto es metadatos).</summary>
    private const double SignatureImageMaxWidth = 115;

    /// <summary>Separación entre imagen de firma y bloque de metadatos.</summary>
    private const double SignatureSidecarGap = 6;

    /// <summary>Tamaño de fuente del bloque de metadatos junto a la firma.</summary>
    private const double SignatureSidecarFontSize = 3;
    public static byte[] RenderPage1(byte[] templatePdf, FurFieldManifest manifest, IReadOnlyDictionary<string, FurFieldValue> values)
    {
        ArgumentNullException.ThrowIfNull(templatePdf);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(values);

        using var input = new MemoryStream(templatePdf);
        using var imported = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();
        var page = output.AddPage(imported.Pages[0]);

        using var gfx = XGraphics.FromPdfPage(page);
        var pageFields = manifest.Fields.Where(f => f.Page == 1);
        foreach (var field in pageFields)
        {
            if (!values.TryGetValue(field.Id, out var value))
                continue;
            DrawField(gfx, field, value);
        }

        using var ms = new MemoryStream();
        output.Save(ms, false);
        return ms.ToArray();
    }

    public static byte[] MergePages(byte[] page1, byte[] page2)
    {
        using var outDoc = new PdfDocument();

        using (var s1 = new MemoryStream(page1))
        using (var d1 = PdfReader.Open(s1, PdfDocumentOpenMode.Import))
            outDoc.AddPage(d1.Pages[0]);

        using (var s2 = new MemoryStream(page2))
        using (var d2 = PdfReader.Open(s2, PdfDocumentOpenMode.Import))
            outDoc.AddPage(d2.Pages[0]);

        using var ms = new MemoryStream();
        outDoc.Save(ms, false);
        return ms.ToArray();
    }

    private static void DrawField(
        XGraphics gfx,
        FurFieldDefinition field,
        FurFieldValue value)
    {
        if (value.ImageBytes is { Length: > 0 })
        {
            DrawSignatureImage(gfx, field, value.ImageBytes, value.ImageSidecarText);
            return;
        }

        switch (field.Type)
        {
            case FurFieldType.Checkbox:
                if (string.Equals(value.Text, "X", StringComparison.OrdinalIgnoreCase))
                    DrawCheckbox(gfx, field);
                break;
            case FurFieldType.Image when value.ImageBytes is { Length: > 0 }:
                DrawImage(gfx, field, value.ImageBytes);
                break;
            case FurFieldType.Multiline:
            case FurFieldType.Text:
                if (!string.IsNullOrWhiteSpace(value.Text))
                    DrawText(gfx, field, value.Text!);
                break;
        }
    }

    private static void DrawCheckbox(XGraphics gfx, FurFieldDefinition field)
    {
        var font = CreateFont(field.FontSize > 0 ? field.FontSize + 2 : 10, true);
        var yBaseline = field.Y + field.Size * 0.85;
        gfx.DrawString("X", font, XBrushes.Black, new XPoint(field.X, yBaseline));
    }

    private static void DrawText(
        XGraphics gfx,
        FurFieldDefinition field,
        string text)
    {
        var font = CreateFont(field.FontSize, field.Bold);
        var lines = field.Type == FurFieldType.Multiline
            ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [text];

        var lineHeight = field.FontSize * 1.25;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var yTop = field.Y + i * lineHeight;
            var drawX = field.X;
            if (field.W > 0 && field.Align != FurTextAlign.Left)
            {
                var size = gfx.MeasureString(line, font);
                drawX = field.Align switch
                {
                    FurTextAlign.Center => field.X + (field.W - size.Width) / 2,
                    FurTextAlign.Right => field.X + field.W - size.Width,
                    _ => field.X,
                };
            }

            var yBaseline = yTop + field.FontSize * 0.82;
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(drawX, yBaseline));
        }
    }

    private static void DrawSignatureImage(
        XGraphics gfx,
        FurFieldDefinition field,
        byte[] imageBytes,
        string? sidecarText)
    {
        var fieldH = field.H > 0 ? field.H : 36;
        var fieldW = field.W > 0 ? field.W : 120;
        var imageW = Math.Min(SignatureImageMaxWidth, fieldW * 0.38);

        DrawImage(gfx, field.X, field.Y, imageW, fieldH, imageBytes);

        if (string.IsNullOrWhiteSpace(sidecarText))
            return;

        var sidecarX = field.X + imageW + SignatureSidecarGap;
        var sidecarW = Math.Max(0, fieldW - imageW - SignatureSidecarGap);
        if (sidecarW <= 0)
            return;

        DrawSidecarText(gfx, sidecarX, field.Y, sidecarW, fieldH, sidecarText);
    }

    private static void DrawSidecarText(
        XGraphics gfx,
        double x,
        double y,
        double w,
        double h,
        string text)
    {
        var font = CreateFont(SignatureSidecarFontSize, bold: false);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lineHeight = SignatureSidecarFontSize * 1.15;
        var maxLines = Math.Max(1, (int)Math.Floor(h / lineHeight));

        for (var i = 0; i < Math.Min(lines.Length, maxLines); i++)
        {
            var line = lines[i];
            if (w > 0)
            {
                line = TruncateToWidth(gfx, line, font, w);
            }

            var yBaseline = y + i * lineHeight + SignatureSidecarFontSize * 0.82;
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(x, yBaseline));
        }
    }

    private static string TruncateToWidth(XGraphics gfx, string line, XFont font, double maxWidth)
    {
        if (gfx.MeasureString(line, font).Width <= maxWidth)
            return line;

        var ellipsis = "…";
        var trimmed = line;
        while (trimmed.Length > 1 && gfx.MeasureString(trimmed + ellipsis, font).Width > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed + ellipsis;
    }

    private static void DrawImage(XGraphics gfx, FurFieldDefinition field, byte[] imageBytes)
    {
        var h = field.H > 0 ? field.H : 36;
        var w = field.W > 0 ? field.W : 120;
        DrawImage(gfx, field.X, field.Y, w, h, imageBytes);
    }

    private static void DrawImage(XGraphics gfx, double x, double y, double w, double h, byte[] imageBytes)
    {
        using var ms = new MemoryStream(imageBytes);
        using var img = XImage.FromStream(() => ms);
        gfx.DrawImage(img, x, y, w, h);
    }

    private static XFont CreateFont(double size, bool bold)
    {
        var style = bold ? XFontStyle.Bold : XFontStyle.Regular;
        return new XFont("Arial", size, style);
    }
}
